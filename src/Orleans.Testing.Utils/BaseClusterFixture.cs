using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Serilog;
using System.Linq;
using Orleans.Storage;
using System.Linq.Expressions;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Concurrency;
using Orleans.Hosting;

namespace SharedOrleansUtils
{
    public class BaseClusterFixture : IDisposable
    {
        public LocalCluster Cluster { get; private set; }
        public IGrainFactory GrainFactory => Cluster.GrainFactory;
        public Task Dispatch(Func<Task> func) => Cluster.Dispatch(func);
        public GrainFactoryMocker Mock => _mocker;
        public IServiceProvider ClusterServices => Cluster.Services;

        private readonly GrainFactoryMocker _mocker;
        private ILogger _logger;
        public ITestOutputHelper OutputHelper
        {
            set
            {
                _logger = new LoggerConfiguration()
                    .WriteTo
                    .Sink(new TestOutputHelperSink(null, value))
                    .CreateLogger();
            }
        }

        public BaseClusterFixture()
        {
            var clusterBuilder = new LocalClusterBuilder();
            clusterBuilder.ConfigureCluster(clusterConfiguration =>
            {
                clusterConfiguration.Globals.ClusterId = "Some-Cluster-Id";
            });
            Log.Logger = new LoggerConfiguration()
                .CreateLogger();

            // Storage provider setup
            var storageProviders = GetType().GetCustomAttributes(typeof(MockStorageProvider), true).Select(p => (MockStorageProvider)p);
            foreach (var provider in storageProviders)
            {
                clusterBuilder.ConfigureCluster(c => c.Globals.RegisterStorageProvider<MockMemoryStorageProvider>(provider.ProviderName));
            }

            // Stream provider setup
            var streamProviders = GetType().GetCustomAttributes(typeof(MockStreamProvider), true).Select(p => (MockStreamProvider)p);
            foreach (var provider in streamProviders)
            {
                clusterBuilder.ConfigureHost(s => s.AddSimpleMessageStreamProvider(provider.ProviderName));
            }

            // Stream storage setup
            var streamStorageProviders = GetType().GetCustomAttributes(typeof(MockStreamStorage), true).Select(p => (MockStreamStorage)p);
            foreach (var storage in streamStorageProviders)
            {
                clusterBuilder.ConfigureHost(s => s.AddMemoryGrainStorage(storage.StorageName));
            }

            OnConfigure(clusterBuilder);
            clusterBuilder.ConfigureHost(OnConfigure);

            clusterBuilder
                .ConfigureServices(s =>
                {
                    s.AddTransient<IGrainFactoryProvider>(services => new GrainFactoryProvider(_mocker.Mock.Object));
                    s.AddTransient(services => _logger);
                    OnConfigureServices(s);
                });

            Cluster = clusterBuilder
                .Build();
            Cluster.StartAsync().Wait();
            _mocker = new GrainFactoryMocker(GrainFactory);
        }

        protected virtual void OnConfigure(LocalClusterBuilder clusterBuilder) { }

        protected virtual void OnConfigure(ISiloHostBuilder siloHostBuilder) { }

        protected virtual void OnConfigureServices(IServiceCollection services) { }

        public async Task<TGrainState> GetGrainState<TGrain, TGrainState>(IGrain grain, string storageProviderName)
            where TGrain : Grain<TGrainState>, IGrain
            where TGrainState : new()
        {
            var storageProvider = ClusterServices.GetServiceByName<IGrainStorage>(storageProviderName);
            var grainState = new GrainState<TGrainState>();
            await storageProvider.ReadStateAsync(typeof(TGrain).FullName, grain as GrainReference, grainState);
            return grainState.State;
        }

        public IStreamProvider GetStreamProvider(string providerName)
        {
            return ClusterServices.GetServiceByName<IStreamProvider>(providerName);
        }

        public async Task PublishToStream<T>(string providerName, Guid streamId, string streamNamespace, T item)
        {
            var requestId = Guid.NewGuid();
            var streamGrain = GrainFactory.GetGrain<IStreamGrain>(requestId);
            await streamGrain.Publish<T>(providerName, streamId, streamNamespace, item);
        }

        public async Task<Task<List<dynamic>>> SubscribeAndGetTaskAwaiter<T>(string providerName, Guid streamId, string streamNamespace, int count)
        {
            var requestId = Guid.NewGuid();
            var streamGrain = GrainFactory.GetGrain<IStreamGrain>(requestId);
            await streamGrain.Subscribe<T>(providerName, streamId, streamNamespace, count);
            return StreamGrain.Tasks[requestId];
        }

        public void Dispose()
        {
            Cluster.Dispose();
        }

        public interface IStreamGrain : IGrainWithGuidKey
        {
            Task Subscribe<T>(string providerName, Guid streamId, string streamNamespace, int threshold);
            Task Publish<T>(string providerName, Guid streamId, string streamNamespace, T item);
        }

        [Reentrant]
        public class StreamGrain : Grain, IStreamGrain
        {
            public static Dictionary<Guid, Task<List<dynamic>>> Tasks { get; } = new Dictionary<Guid, Task<List<dynamic>>>();

            public async Task Publish<T>(string providerName, Guid streamId, string streamNamespace, T item)
            {
                var provider = GetStreamProvider(providerName);
                var stream = provider.GetStream<T>(streamId, streamNamespace);
                await stream.OnNextAsync(item);
            }

            public async Task Subscribe<T>(string providerName, Guid streamId, string streamNamespace, int threshold)
            {
                var requestId = this.GetPrimaryKey();
                var tcs = new TaskCompletionSource<List<dynamic>>();
                Tasks[requestId] = tcs.Task;

                var eventCount = 0;
                var list = new List<dynamic>();

                var provider = GetStreamProvider(providerName);
                var stream = provider.GetStream<T>(streamId, streamNamespace);
                await stream.SubscribeAsync((item, token) =>
                {
                    list.Add(item);
                    if (++eventCount >= threshold)
                        tcs.TrySetResult(list);

                    return Task.CompletedTask;
                });
            }
        }

        public class GrainFactoryMocker
        {
            private readonly IGrainFactory _grainFactory;
            private readonly Dictionary<Type, Dictionary<NullObject<Guid?>, Mock>> _mockGrainWithGuidKeyRegistry = new Dictionary<Type, Dictionary<NullObject<Guid?>, Mock>>();
            private readonly Dictionary<Type, Dictionary<(Guid?, string), Mock>> _mockGrainWithGuidCompoundKeyRegistry = new Dictionary<Type, Dictionary<(Guid?, string), Mock>>();

            internal Mock<IGrainFactory> Mock { get; private set; }

            internal GrainFactoryMocker(IGrainFactory grainFactory)
            {
                Mock = new Mock<IGrainFactory>();
                _grainFactory = grainFactory;
            }

            public Task GetAwaiterForGuidGrainMethod<TGrain, TResult>(Guid? primaryKey, Expression<Func<TGrain, TResult>> expression, TResult result)
                where TGrain : class, IGrainWithGuidKey
            {
                var mockGrain = GetGuidGrain<TGrain>(primaryKey);
                var tcs = new TaskCompletionSource<object>();
                mockGrain
                    .Setup(expression)
                    .Callback(() => tcs.SetResult(null))
                    .Returns(result);
                return tcs.Task;
            }

            public void VerifyGuidGrainActivated<TGrain>(Guid primaryKey)
                where TGrain : class, IGrainWithGuidKey
            {
                Mock.Verify(x => x.GetGrain<TGrain>(It.Is<Guid>(pkey => pkey == primaryKey), It.IsAny<string>()));
            }

            public void VerifyGuidGrainActivated<TGrain>(Guid primaryKey, Times times)
                where TGrain : class, IGrainWithGuidKey
            {
                Mock.Verify(x => x.GetGrain<TGrain>(It.Is<Guid>(pkey => pkey == primaryKey), It.IsAny<string>()), times);
            }

            public void VerifyGuidGrainActivated<TGrain>(IEnumerable<Guid> primaryKeys)
                where TGrain : class, IGrainWithGuidKey
            {
                foreach (Guid guid in primaryKeys)
                {
                    Mock.Verify(x => x.GetGrain<TGrain>(It.Is<Guid>(pkey => pkey == guid), It.IsAny<string>()));
                }
            }

            public void VerifyGuidGrainActivated<TGrain>(IEnumerable<Guid> primaryKeys, Times times)
                where TGrain : class, IGrainWithGuidKey
            {
                foreach (Guid guid in primaryKeys)
                {
                    Mock.Verify(x => x.GetGrain<TGrain>(It.Is<Guid>(pkey => pkey == guid), It.IsAny<string>()), times);
                }
            }

            public void VerifyGuidCompoundGrainActivated<TGrain>(Guid primaryKey, string compoundKey)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                Mock.Verify(x => x.GetGrain<TGrain>(
                    It.Is<Guid>(guid => guid == primaryKey),
                    It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                    It.IsAny<string>()));
            }

            public void VerifyGuidCompoundGrainActivated<TGrain>(Guid primaryKey, string compoundKey, Times times)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                Mock.Verify(x => x.GetGrain<TGrain>(
                    It.Is<Guid>(guid => guid == primaryKey),
                    It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                    It.IsAny<string>()), times);
            }

            public void VerifyGuidCompoundGrainActivated<TGrain>(IEnumerable<(Guid PrimaryKey, string CompoundKey)> keys)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                foreach (var (primaryKey, compoundKey) in keys)
                {
                    Mock.Verify(x => x.GetGrain<TGrain>(
                        It.Is<Guid>(guid => guid == primaryKey),
                        It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                        It.IsAny<string>()));
                }
            }

            public void VerifyGuidCompoundGrainActivated<TGrain>(IEnumerable<(Guid PrimaryKey, string CompoundKey)> keys, Times times)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                foreach (var (primaryKey, compoundKey) in keys)
                {
                    Mock.Verify(x => x.GetGrain<TGrain>(
                        It.Is<Guid>(guid => guid == primaryKey),
                        It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                        It.IsAny<string>()), times);
                }
            }

            public GrainFactoryMocker Clear()
            {
                foreach (var g in _mockGrainWithGuidKeyRegistry.SelectMany(d => d.Value.Values))
                {
                    g.Reset();
                }
                foreach (var g in _mockGrainWithGuidCompoundKeyRegistry.SelectMany(d => d.Value.Values))
                {
                    g.Reset();
                }

                _mockGrainWithGuidKeyRegistry.Clear();
                _mockGrainWithGuidCompoundKeyRegistry.Clear();

                return this;
            }

            public GrainFactoryMocker PassThruGuidGrain<TGrain>(Guid? primaryKey = null)
                where TGrain : class, IGrainWithGuidKey
            {
                Mock
                    .Setup(x => x.GetGrain<TGrain>(
                        It.Is<Guid>(id => primaryKey.HasValue ? id == primaryKey : true),
                        It.IsAny<string>()))
                    .Returns((Guid g, string c) => _grainFactory.GetGrain<TGrain>(g, c));
                return this;
            }

            public GrainFactoryMocker PassThruGuidCompoundGrain<TGrain>(Guid? guid, string id)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                Mock
                    .Setup(x => x.GetGrain<TGrain>(
                        It.Is<Guid>(gid => guid.HasValue ? gid == guid : true),
                        It.Is<string>(sid => !string.IsNullOrEmpty(id) ? id.Equals(sid, StringComparison.InvariantCulture) : true),
                        It.IsAny<string>()))
                    .Returns((Guid g, string s, string c) => _grainFactory.GetGrain<TGrain>(g, s, c));
                return this;
            }

            public Mock<TGrain> GetGuidGrain<TGrain>(Guid? guid = null)
                where TGrain : class, IGrainWithGuidKey
            {
                return _mockGrainWithGuidKeyRegistry[typeof(TGrain)]?[guid] as Mock<TGrain>;
            }

            public Mock<TGrain> GetGuidCompoundGrain<TGrain>(Guid? guid = null, string secondaryKey = null)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                return _mockGrainWithGuidCompoundKeyRegistry[typeof(TGrain)]?[(guid, secondaryKey)] as Mock<TGrain>;
            }

            public GrainFactoryMocker GuidGrain<TGrain>(Action<Mock<TGrain>> mocker = null, Action<Guid> callback = null)
                where TGrain : class, IGrainWithGuidKey
            {
                return GuidGrain(null, mocker, callback);
            }

            public GrainFactoryMocker ClearGuidGrain<TGrain>(Guid? guid = null)
                where TGrain : class, IGrainWithGuidKey
            {
                if (!_mockGrainWithGuidKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
                {
                    return this;
                }

                registry.Remove(guid);

                return this;
            }

            public GrainFactoryMocker GuidGrain<TGrain>(Guid? guid, Action<Mock<TGrain>> mocker = null, Action<Guid> callback = null)
                where TGrain : class, IGrainWithGuidKey
            {
                var mockGrain = new Mock<TGrain>
                {
                    DefaultValue = DefaultValue.Mock
                };

                if (!_mockGrainWithGuidKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
                {
                    registry = new Dictionary<NullObject<Guid?>, Mock>();
                    _mockGrainWithGuidKeyRegistry.Add(typeof(TGrain), registry);
                }

                // We've already mocked this grain
                if (registry.ContainsKey(guid)) throw new Exception("GuidGrain already mocked - you must call ClearGuidGrain() first");

                registry.Add(guid, mockGrain);
                mocker?.Invoke(mockGrain);

                Mock
                    .Setup(x => x.GetGrain<TGrain>(It.Is<Guid>(g => guid.HasValue ? g == guid : true), It.IsAny<string>()))
                    .Callback((Guid g, string c) => callback?.Invoke(g))
                    .Returns(() =>
                    {
                        return mockGrain.Object;
                    });

                return this;
            }

            public GrainFactoryMocker GuidCompoundGrain<TGrain>(Action<Mock<TGrain>> mocker = null, Action<Guid, string> callback = null)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                return GuidCompoundGrain(null, null, mocker, callback);
            }

            public GrainFactoryMocker GuidCompoundGrain<TGrain>(Guid primaryKey, Action<Mock<TGrain>> mocker = null, Action<Guid, string> callback = null)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                return GuidCompoundGrain(primaryKey, null, mocker, callback);
            }

            public GrainFactoryMocker GuidCompoundGrain<TGrain>(string compoundKey, Action<Mock<TGrain>> mocker = null, Action<Guid, string> callback = null)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                return GuidCompoundGrain(null, compoundKey, mocker, callback);
            }

            public GrainFactoryMocker ClearGuidCompoundGrain<TGrain>(Guid? primaryKey = null, string compoundKey = null)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                if (!_mockGrainWithGuidCompoundKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
                {
                    return this;
                }

                var key = (primaryKey, compoundKey);
                registry.Remove(key);

                return this;
            }

            public GrainFactoryMocker GuidCompoundGrain<TGrain>(Guid? primaryKey = null, string compoundKey = null, Action<Mock<TGrain>> mocker = null, Action<Guid, string> callback = null)
                where TGrain : class, IGrainWithGuidCompoundKey
            {
                var mockGrain = new Mock<TGrain>
                {
                    DefaultValue = DefaultValue.Mock
                };

                if (!_mockGrainWithGuidCompoundKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
                {
                    registry = new Dictionary<(Guid?, string), Mock>();
                    _mockGrainWithGuidCompoundKeyRegistry.Add(typeof(TGrain), registry);
                }

                var key = (primaryKey, compoundKey);

                // We've already mocked this grain
                if (registry.ContainsKey(key)) throw new Exception("GuidCompoundGrain already mocked - you must call ClearGuidCompoundGrain() first");

                registry.Add(key, mockGrain);
                mocker?.Invoke(mockGrain);

                Mock
                    .Setup(x => x.GetGrain<TGrain>(
                        It.Is<Guid>(g => primaryKey.HasValue ? g == primaryKey : true),
                        It.Is<string>(s => !string.IsNullOrEmpty(compoundKey) ? compoundKey.Equals(s, StringComparison.InvariantCulture) : true),
                        It.IsAny<string>()))
                    .Callback((Guid g, string k, string c) => callback?.Invoke(g, k))
                    .Returns(() =>
                    {
                        return mockGrain.Object;
                    });

                return this;
            }
        }
    }

    public class BaseClusterFixture<T1> : BaseClusterFixture
        where T1 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
        }
    }

    public class BaseClusterFixture<T1, T2> : BaseClusterFixture
        where T1 : class
        where T2 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };
        public Mock<T2> Dependency2 { get; } = new Mock<T2>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
            services.AddTransient(s => Dependency2.Object);
        }
    }

    public class BaseClusterFixture<T1, T2, T3> : BaseClusterFixture
        where T1 : class
        where T2 : class
        where T3 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };
        public Mock<T2> Dependency2 { get; } = new Mock<T2>() { DefaultValue = DefaultValue.Mock };
        public Mock<T3> Dependency3 { get; } = new Mock<T3>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
            services.AddTransient(s => Dependency2.Object);
            services.AddTransient(s => Dependency3.Object);
        }
    }

    public class BaseClusterFixture<T1, T2, T3, T4> : BaseClusterFixture
        where T1 : class
        where T2 : class
        where T3 : class
        where T4 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };
        public Mock<T2> Dependency2 { get; } = new Mock<T2>() { DefaultValue = DefaultValue.Mock };
        public Mock<T3> Dependency3 { get; } = new Mock<T3>() { DefaultValue = DefaultValue.Mock };
        public Mock<T4> Dependency4 { get; } = new Mock<T4>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
            services.AddTransient(s => Dependency2.Object);
            services.AddTransient(s => Dependency3.Object);
            services.AddTransient(s => Dependency4.Object);
        }
    }

    public class BaseClusterFixture<T1, T2, T3, T4, T5> : BaseClusterFixture
        where T1 : class
        where T2 : class
        where T3 : class
        where T4 : class
        where T5 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };
        public Mock<T2> Dependency2 { get; } = new Mock<T2>() { DefaultValue = DefaultValue.Mock };
        public Mock<T3> Dependency3 { get; } = new Mock<T3>() { DefaultValue = DefaultValue.Mock };
        public Mock<T4> Dependency4 { get; } = new Mock<T4>() { DefaultValue = DefaultValue.Mock };
        public Mock<T5> Dependency5 { get; } = new Mock<T5>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
            services.AddTransient(s => Dependency2.Object);
            services.AddTransient(s => Dependency3.Object);
            services.AddTransient(s => Dependency4.Object);
            services.AddTransient(s => Dependency5.Object);
        }
    }

    public class BaseClusterFixture<T1, T2, T3, T4, T5, T6> : BaseClusterFixture
        where T1 : class
        where T2 : class
        where T3 : class
        where T4 : class
        where T5 : class
        where T6 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };
        public Mock<T2> Dependency2 { get; } = new Mock<T2>() { DefaultValue = DefaultValue.Mock };
        public Mock<T3> Dependency3 { get; } = new Mock<T3>() { DefaultValue = DefaultValue.Mock };
        public Mock<T4> Dependency4 { get; } = new Mock<T4>() { DefaultValue = DefaultValue.Mock };
        public Mock<T5> Dependency5 { get; } = new Mock<T5>() { DefaultValue = DefaultValue.Mock };
        public Mock<T6> Dependency6 { get; } = new Mock<T6>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
            services.AddTransient(s => Dependency2.Object);
            services.AddTransient(s => Dependency3.Object);
            services.AddTransient(s => Dependency4.Object);
            services.AddTransient(s => Dependency5.Object);
            services.AddTransient(s => Dependency6.Object);
        }
    }

    public class BaseClusterFixture<T1, T2, T3, T4, T5, T6, T7> : BaseClusterFixture
        where T1 : class
        where T2 : class
        where T3 : class
        where T4 : class
        where T5 : class
        where T6 : class
        where T7 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };
        public Mock<T2> Dependency2 { get; } = new Mock<T2>() { DefaultValue = DefaultValue.Mock };
        public Mock<T3> Dependency3 { get; } = new Mock<T3>() { DefaultValue = DefaultValue.Mock };
        public Mock<T4> Dependency4 { get; } = new Mock<T4>() { DefaultValue = DefaultValue.Mock };
        public Mock<T5> Dependency5 { get; } = new Mock<T5>() { DefaultValue = DefaultValue.Mock };
        public Mock<T6> Dependency6 { get; } = new Mock<T6>() { DefaultValue = DefaultValue.Mock };
        public Mock<T7> Dependency7 { get; } = new Mock<T7>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
            services.AddTransient(s => Dependency2.Object);
            services.AddTransient(s => Dependency3.Object);
            services.AddTransient(s => Dependency4.Object);
            services.AddTransient(s => Dependency5.Object);
            services.AddTransient(s => Dependency6.Object);
            services.AddTransient(s => Dependency7.Object);
        }
    }

    public class BaseClusterFixture<T1, T2, T3, T4, T5, T6, T7, T8> : BaseClusterFixture
        where T1 : class
        where T2 : class
        where T3 : class
        where T4 : class
        where T5 : class
        where T6 : class
        where T7 : class
        where T8 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };
        public Mock<T2> Dependency2 { get; } = new Mock<T2>() { DefaultValue = DefaultValue.Mock };
        public Mock<T3> Dependency3 { get; } = new Mock<T3>() { DefaultValue = DefaultValue.Mock };
        public Mock<T4> Dependency4 { get; } = new Mock<T4>() { DefaultValue = DefaultValue.Mock };
        public Mock<T5> Dependency5 { get; } = new Mock<T5>() { DefaultValue = DefaultValue.Mock };
        public Mock<T6> Dependency6 { get; } = new Mock<T6>() { DefaultValue = DefaultValue.Mock };
        public Mock<T7> Dependency7 { get; } = new Mock<T7>() { DefaultValue = DefaultValue.Mock };
        public Mock<T8> Dependency8 { get; } = new Mock<T8>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
            services.AddTransient(s => Dependency2.Object);
            services.AddTransient(s => Dependency3.Object);
            services.AddTransient(s => Dependency4.Object);
            services.AddTransient(s => Dependency5.Object);
            services.AddTransient(s => Dependency6.Object);
            services.AddTransient(s => Dependency7.Object);
            services.AddTransient(s => Dependency8.Object);
        }
    }

    public class BaseClusterFixture<T1, T2, T3, T4, T5, T6, T7, T8, T9> : BaseClusterFixture
        where T1 : class
        where T2 : class
        where T3 : class
        where T4 : class
        where T5 : class
        where T6 : class
        where T7 : class
        where T8 : class
        where T9 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>();
        public Mock<T2> Dependency2 { get; } = new Mock<T2>();
        public Mock<T3> Dependency3 { get; } = new Mock<T3>();
        public Mock<T4> Dependency4 { get; } = new Mock<T4>();
        public Mock<T5> Dependency5 { get; } = new Mock<T5>();
        public Mock<T6> Dependency6 { get; } = new Mock<T6>();
        public Mock<T7> Dependency7 { get; } = new Mock<T7>();
        public Mock<T8> Dependency8 { get; } = new Mock<T8>();
        public Mock<T9> Dependency9 { get; } = new Mock<T9>();

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
            services.AddTransient(s => Dependency2.Object);
            services.AddTransient(s => Dependency3.Object);
            services.AddTransient(s => Dependency4.Object);
            services.AddTransient(s => Dependency5.Object);
            services.AddTransient(s => Dependency6.Object);
            services.AddTransient(s => Dependency7.Object);
            services.AddTransient(s => Dependency8.Object);
            services.AddTransient(s => Dependency9.Object);
        }
    }

    [Serializable]
    public class GrainState<T> : IGrainState
    {
        public T State;

        object IGrainState.State
        {
            get
            {
                return State;

            }
            set
            {
                State = (T)value;
            }
        }

        /// <inheritdoc />
        public string ETag { get; set; }

        /// <summary>Initializes a new instance of <see cref="GrainState{T}"/>.</summary>
        public GrainState()
        {
        }

        /// <summary>Initializes a new instance of <see cref="GrainState{T}"/>.</summary>
        /// <param name="state"> The initial value of the state.</param>
        public GrainState(T state) : this(state, null)
        {
        }

        /// <summary>Initializes a new instance of <see cref="GrainState{T}"/>.</summary>
        /// <param name="state">The initial value of the state.</param>
        /// <param name="eTag">The initial e-tag value that allows optimistic concurrency checks at the storage provider level.</param>
        public GrainState(T state, string eTag)
        {
            State = state;
            ETag = eTag;
        }
    }
}
