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
using Orleans.Runtime;
using Orleans.Streams;
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

        public BaseClusterFixture(int siloPort = 11111, int gatewayPort = 30000, Guid? serviceId = null, string clusterId = null)
        {
            var clusterBuilder = new LocalClusterBuilder(siloPort, gatewayPort, serviceId, clusterId);
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
}
