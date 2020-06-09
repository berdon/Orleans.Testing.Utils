using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans;
using Orleans.CodeGeneration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.Streams;
using Orleans.Utilities;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Orleans.Testing.Utils
{
    public class ClusterFixture : IDisposable
    {
        private GrainFactoryMocker _mocker;
        private ILogger _logger;

        public LocalCluster Cluster { get; private set; }

        public IGrainFactory GrainFactory
        {
            get
            {
                EnsureClusterIsRunning();
                return Cluster.GrainFactory;
            }
        }

        public GrainFactoryMocker Mock
        {
            get
            {
                EnsureClusterIsRunning();
                return _mocker;
            }
        }

        public IServiceProvider ClusterServices
        {
            get
            {
                EnsureClusterIsRunning();
                return Cluster.Services;
            }
        }

        public ITestOutputHelper OutputHelper
        {
            set
            {
                _logger = new LoggerConfiguration()
                    .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
                    .WriteTo
                    .Sink(new TestOutputHelperSink(null, value))
                    .CreateLogger();

                Log.Logger = _logger;
            }
        }
        public ILogger Logger => _logger;

        public ILogger CreateLogger<T>() => _logger.ForContext<T>();

        public virtual async Task Start(int siloPort = 11111, int gatewayPort = 30000, string serviceId = null, string clusterId = null)
        {
            EnsureClusterIsNotRunning();
            var clusterBuilder = new LocalClusterBuilder(siloPort, gatewayPort, serviceId, clusterId);

            // Storage provider setup
            var storageProviders = GetType().GetCustomAttributes(typeof(MockStorageProvider), true).Select(p => (MockStorageProvider)p);
            foreach (var provider in storageProviders)
            {
                clusterBuilder.ConfigureServices(services =>
                {
                    services.AddSingletonNamedService<IGrainStorage>(provider.ProviderName, (sp, n) => ActivatorUtilities.CreateInstance<MockMemoryStorageProvider>(sp, provider.ProviderName));

                    if (typeof(ILifecycleParticipant<ISiloLifecycle>).IsAssignableFrom(typeof(MockMemoryStorageProvider)))
                    {
                        services.AddSingletonNamedService<ILifecycleParticipant<ISiloLifecycle>>(provider.ProviderName, (svc, n) => (ILifecycleParticipant<ISiloLifecycle>)svc.GetRequiredServiceByName<IGrainStorage>(provider.ProviderName));
                    }

                    if (typeof(IControllable).IsAssignableFrom(typeof(MockMemoryStorageProvider)))
                    {
                        services.AddSingletonNamedService<IControllable>(provider.ProviderName, (svc, n) => (IControllable)svc.GetRequiredServiceByName<IGrainStorage>(provider.ProviderName));
                    }
                });
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

            Cluster = clusterBuilder.Build();
            await Cluster.StartAsync();
            _mocker = new GrainFactoryMocker(GrainFactory);
            await OnReady();
        }

        public void Dispose()
        {
            EnsureClusterIsRunning();
            Cluster.Dispose();
        }

        protected virtual void OnConfigure(LocalClusterBuilder clusterBuilder) { }

        protected virtual void OnConfigure(ISiloHostBuilder siloHostBuilder) { }

        protected virtual void OnConfigureServices(IServiceCollection services) { }

        protected virtual Task OnReady() => Task.CompletedTask;

        public Task Dispatch(Func<Task> func)
        {
            EnsureClusterIsRunning();
            return Cluster.Dispatch(func);
        }

        public Task Dispatch<TFixture>(Func<TFixture, Task> func) where TFixture : ClusterFixture
        {
            EnsureClusterIsRunning();
            return Cluster.Dispatch(() => func((TFixture)this));
        }

        public async Task<TGrainState> GetGrainState<TGrain, TGrainState>(IGrain grain, string storageProviderName)
            where TGrain : Grain<TGrainState>, IGrain
            where TGrainState : new()
        {
            EnsureClusterIsRunning();

            var storageProvider = ClusterServices.GetServiceByName<IGrainStorage>(storageProviderName);
            var grainState = new GrainState<TGrainState>();
            await storageProvider.ReadStateAsync(typeof(TGrain).FullName, grain as GrainReference, grainState);
            return grainState.State;
        }

        public async Task SetGrainState<TGrain, TGrainState>(IGrain grain, TGrainState grainState, string storageProviderName)
            where TGrain : Grain<TGrainState>, IGrain
            where TGrainState : new()
        {
            EnsureClusterIsRunning();

            var storageProvider = ClusterServices.GetServiceByName<IGrainStorage>(storageProviderName);
            await storageProvider.WriteStateAsync(typeof(TGrain).FullName, grain as GrainReference, new GrainState<TGrainState>(grainState));
        }

        public Task WaitForStateOperationAsync<TGrain, TGrainState>(IGrain grain, MockMemoryStorageProvider.Operation operation, string storageProviderName, int calls = 1, object args = null)
            where TGrain : Grain<TGrainState>, IGrain
            where TGrainState : new()
        {
            var storageProvider = ClusterServices.GetServiceByName<IGrainStorage>(storageProviderName) as MockMemoryStorageProvider;
            if (storageProvider == null) throw new Exception("Storage provider must be a MockMemoryStorageProvider");
            return storageProvider.GetOperationAwaitable(typeof(TGrain).FullName, grain as GrainReference, operation, calls, args);
        }

        public IStreamProvider GetStreamProvider(string providerName)
        {
            EnsureClusterIsRunning();

            return ClusterServices.GetServiceByName<IStreamProvider>(providerName);
        }

        public async Task PublishToStream<T>(string providerName, Guid streamId, string streamNamespace, T item)
        {
            EnsureClusterIsRunning();

            _logger?.Verbose("Publishing {Item} to {Provider}://{Namespace}-{Id}", typeof(T), streamNamespace, streamId);

            var requestId = Guid.NewGuid();
            var streamGrain = GrainFactory.GetGrain<IStreamGrain>(requestId);
            await streamGrain.Publish<T>(providerName, streamId, streamNamespace, item);
        }

        public async Task<Task<List<dynamic>>> SubscribeAndGetTaskAwaiter<T>(string providerName, Guid streamId, string streamNamespace, int count)
        {
            EnsureClusterIsRunning();

            var requestId = Guid.NewGuid();
            var streamGrain = GrainFactory.GetGrain<IStreamGrain>(requestId);
            await streamGrain.Subscribe<T>(providerName, streamId, streamNamespace, count);
            return StreamGrain.Tasks[requestId];
        }

        public Task InvokeMethodAsync<TGrain>(Expression<Func<TGrain, Task>> method, Guid primaryKey, params object[] arguments)
            where TGrain : IGrainWithGuidKey
        {
            EnsureClusterIsRunning();

            var grain = GrainFactory.GetGrain<TGrain>(primaryKey);
            var grainReferenceRuntime = ClusterServices.GetRequiredService<IGrainReferenceRuntime>();
            var methodId = GetMethodId(method);
            return grainReferenceRuntime.InvokeMethodAsync<object>((GrainReference)(object)grain, methodId, arguments, InvokeMethodOptions.None, null);
        }

        public Task<TResult> InvokeMethodAsync<TGrain, TResult>(Expression<Func<TGrain, TResult>> method, Guid primaryKey, params object[] arguments)
            where TGrain : IGrainWithGuidKey
        {
            EnsureClusterIsRunning();

            var grain = GrainFactory.GetGrain<TGrain>(primaryKey);
            var grainReferenceRuntime = ClusterServices.GetRequiredService<IGrainReferenceRuntime>();
            var methodId = GetMethodId(method);
            return grainReferenceRuntime.InvokeMethodAsync<TResult>((GrainReference)(object)grain, methodId, arguments, InvokeMethodOptions.None, null);
        }

        public Task InvokeMethodAsync<TGrain>(Expression<Func<TGrain, Task>> method, Guid primaryKey, string secondaryKey, params object[] arguments)
            where TGrain : IGrainWithGuidCompoundKey
        {
            EnsureClusterIsRunning();

            var grain = GrainFactory.GetGrain<TGrain>(primaryKey, secondaryKey);
            var grainReferenceRuntime = ClusterServices.GetRequiredService<IGrainReferenceRuntime>();
            var methodId = GetMethodId(method);
            return grainReferenceRuntime.InvokeMethodAsync<object>((GrainReference)(object)grain, methodId, arguments, InvokeMethodOptions.None, null);
        }

        public Task<TResult> InvokeMethodAsync<TGrain, TResult>(Expression<Func<TGrain, TResult>> method, Guid primaryKey, string secondaryKey, params object[] arguments)
            where TGrain : IGrainWithGuidCompoundKey
        {
            EnsureClusterIsRunning();

            var grain = GrainFactory.GetGrain<TGrain>(primaryKey, secondaryKey);
            var grainReferenceRuntime = ClusterServices.GetRequiredService<IGrainReferenceRuntime>();
            var methodId = GetMethodId(method);
            return grainReferenceRuntime.InvokeMethodAsync<TResult>((GrainReference)(object)grain, methodId, arguments, InvokeMethodOptions.None, null);
        }

        private static int GetMethodId<TGrain, TResult>(Expression<Func<TGrain, TResult>> method)
        {
            var methodCall = method.Body as MethodCallExpression;
            var methodInfo = methodCall.Method;

            var attr = methodInfo.GetCustomAttribute<MethodIdAttribute>(true);
            if (attr != null) return attr.MethodId;

            var result = FormatMethodForIdComputation(methodInfo);
            return Orleans.Runtime.Utils.CalculateIdHash(result);
        }

        // Straight outta compton
        // https://github.com/dotnet/orleans/blob/master/src/Orleans.Core/CodeGeneration/GrainInterfaceUtils.cs
        private static string FormatMethodForIdComputation(MethodInfo methodInfo)
        {
            var strMethodId = new StringBuilder(methodInfo.Name);

            if (methodInfo.IsGenericMethodDefinition)
            {
                strMethodId.Append('<');
                var first = true;
                foreach (var arg in methodInfo.GetGenericArguments())
                {
                    if (!first) strMethodId.Append(',');
                    else first = false;
                    strMethodId.Append(RuntimeTypeNameFormatter.Format(arg));
                }

                strMethodId.Append('>');
            }

            strMethodId.Append('(');
            ParameterInfo[] parameters = methodInfo.GetParameters();
            bool bFirstTime = true;
            foreach (ParameterInfo info in parameters)
            {
                if (!bFirstTime)
                    strMethodId.Append(',');
                var pt = info.ParameterType;
                if (pt.IsGenericParameter)
                {
                    strMethodId.Append(pt.Name);
                }
                else
                {
                    strMethodId.Append(RuntimeTypeNameFormatter.Format(info.ParameterType));
                }

                bFirstTime = false;
            }

            strMethodId.Append(')');
            var result = strMethodId.ToString();
            return result;
        }

        private void EnsureClusterIsRunning()
        {
            if (Cluster == null)
            {
                throw new NotSupportedException("Cluster is not running.");
            }
        }

        private void EnsureClusterIsNotRunning()
        {
            if (Cluster != null)
            {
                throw new NotSupportedException("Cluster is already running.");
            }
        }
    }

    public class ClusterFixture<T1> : ClusterFixture
        where T1 : class
    {
        public Mock<T1> Dependency1 { get; } = new Mock<T1>() { DefaultValue = DefaultValue.Mock };

        protected override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);
            services.AddTransient(s => Dependency1.Object);
        }
    }

    public class ClusterFixture<T1, T2> : ClusterFixture
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

    public class ClusterFixture<T1, T2, T3> : ClusterFixture
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

    public class ClusterFixture<T1, T2, T3, T4> : ClusterFixture
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

    public class ClusterFixture<T1, T2, T3, T4, T5> : ClusterFixture
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

    public class ClusterFixture<T1, T2, T3, T4, T5, T6> : ClusterFixture
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

    public class ClusterFixture<T1, T2, T3, T4, T5, T6, T7> : ClusterFixture
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

    public class ClusterFixture<T1, T2, T3, T4, T5, T6, T7, T8> : ClusterFixture
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

    public class ClusterFixture<T1, T2, T3, T4, T5, T6, T7, T8, T9> : ClusterFixture
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
