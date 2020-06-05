using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.ApplicationParts;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Testing.Utils
{
    public class LocalCluster : IDisposable
    {
        internal static TaskScheduler OrleansScheduler;

        private readonly ISiloHost _siloHost;
        
        public IGrainFactory GrainFactory => _siloHost.Services.GetRequiredService<IProviderRuntime>().GrainFactory;
        public IServiceProvider Services => _siloHost.Services;

        internal LocalCluster(ISiloHost siloHost)
        {
            _siloHost = siloHost;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await _siloHost.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return _siloHost.StopAsync(cancellationToken);
        }

        public async void Dispose()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await StopAsync(cts.Token);
            }
        }

        public Task Dispatch(Func<Task> func)
        {
            if (OrleansScheduler == null) throw new Exception("Hrm derp?");
            return Task.Factory.StartNew(func, CancellationToken.None, TaskCreationOptions.None, scheduler: OrleansScheduler).Unwrap();
        }
    }

    public class LocalClusterBuilder
    {
        private readonly (int SiloPort, int GatewayPort) _ports;
        private readonly (string ServiceId, string ClusterId) _clusterOptions;
        private readonly ISiloHostBuilder _builder;
        private readonly List<Action<IServiceCollection>> _serviceDelegates = new List<Action<IServiceCollection>>();
        private Action<IApplicationPartManager> _partDelegate;

        public LocalClusterBuilder(int siloPort = 11111, int gatewayPort = 30000, string serviceId = null, string clusterId = null)
        {
            serviceId ??= Guid.NewGuid().ToString();
            clusterId ??= Guid.NewGuid().ToString();

            _ports = (siloPort, gatewayPort);
            _clusterOptions = (serviceId, clusterId);
            _builder = new SiloHostBuilder();
        }

        public LocalClusterBuilder ConfigureHost(Action<ISiloHostBuilder> configDelegate)
        {
            configDelegate?.Invoke(_builder);
            return this;
        }

        public LocalClusterBuilder ConfigureServices(Action<IServiceCollection> serviceDelegate)
        {
            _serviceDelegates.Add(serviceDelegate);
            return this;
        }

        public LocalClusterBuilder ConfigureApplicationParts(Action<IApplicationPartManager> partsDelegate)
        {
            _partDelegate = partsDelegate;
            return this;
        }

        public LocalCluster Build()
        {
            var builder = _builder
                .UseLocalhostClustering(_ports.SiloPort, _ports.GatewayPort, serviceId: _clusterOptions.ServiceId, clusterId: _clusterOptions.ClusterId)
                .Configure<MessagingOptions>(x => x.PropagateActivityId = true)
                .ConfigureApplicationParts(appParts =>
                {
                    appParts.AddFromApplicationBaseDirectory().WithReferences();
                    appParts.AddFromAppDomain().WithReferences();
                    _partDelegate?.Invoke(appParts);
                })
                .AddStartupTask((services, cancellationToken) =>
                {
                    LocalCluster.OrleansScheduler = TaskScheduler.Current;
                    return Task.CompletedTask;
                });

            foreach (var serviceDelegate in _serviceDelegates)
                builder = builder.ConfigureServices(serviceDelegate);

            var siloHost = builder.Build();
            return new LocalCluster(siloHost);
        }
    }
}
