using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.ApplicationParts;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime.Configuration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharedOrleansUtils
{
    public class LocalCluster : IDisposable
    {
        private readonly ISiloHost _siloHost;
        private TaskScheduler _orleansScheduler;
        public IGrainFactory GrainFactory => _siloHost.Services.GetRequiredService<IProviderRuntime>().GrainFactory;

        internal LocalCluster(ISiloHost siloHost)
        {
            _siloHost = siloHost;
        }

        public async Task StartAsync()
        {
            await _siloHost.StartAsync();
            _orleansScheduler = LocalClusterBootstrap.OrleansScheduler;
        }

        public Task StopAsync()
        {
            return _siloHost.StopAsync();
        }

        public void Dispose()
        {
            _siloHost.StopAsync().Wait();
        }

        public Task Dispatch(Func<Task> func)
        {
            if (_orleansScheduler == null) throw new Exception("Hrm derp?");
            return Task.Factory.StartNew(func, CancellationToken.None, TaskCreationOptions.None, scheduler: _orleansScheduler).Unwrap();
        }

        public IServiceProvider Services => _siloHost.Services;
    }

    public class LocalClusterBuilder
    {
        private readonly ClusterConfiguration _config;
        private readonly ISiloHostBuilder _builder;
        private Action<IServiceCollection> _serviceDelegate;
        private Action<IApplicationPartManager> _partDelegate;

        public LocalClusterBuilder()
        {
            _config = ClusterConfiguration.LocalhostPrimarySilo(11111, 30000);
            _config.Defaults.PropagateActivityId = true;
            _config.Globals.RegisterBootstrapProvider<LocalClusterBootstrap>(nameof(LocalClusterBootstrap));
            _builder = new SiloHostBuilder();
        }

        public LocalClusterBuilder ConfigureCluster(Action<ClusterConfiguration> configDelegate)
        {
            configDelegate?.Invoke(_config);
            return this;
        }

        public LocalClusterBuilder ConfigureHost(Action<ISiloHostBuilder> configDelegate)
        {
            configDelegate?.Invoke(_builder);
            return this;
        }

        public LocalClusterBuilder ConfigureServices(Action<IServiceCollection> serviceDelegate)
        {
            _serviceDelegate = serviceDelegate;
            return this;
        }

        public LocalClusterBuilder ConfigureApplicationParts(Action<IApplicationPartManager> partsDelegate)
        {
            _partDelegate = partsDelegate;
            return this;
        }

        public LocalCluster Build()
        {
            var siloHost = _builder
                .UseConfiguration(_config)
                //.ConfigureSiloName("Test Cluster")
                .ConfigureApplicationParts(appParts =>
                {
                    appParts.AddFromApplicationBaseDirectory().WithReferences();
                    appParts.AddFromAppDomain().WithReferences();
                    _partDelegate?.Invoke(appParts);
                })
                .ConfigureServices(_serviceDelegate)
                .Build();

            return new LocalCluster(siloHost);
        }
    }

    internal class LocalClusterBootstrap : IBootstrapProvider
    {
        internal static TaskScheduler OrleansScheduler;

        public string Name { get; set; }

        public Task Init(string name, IProviderRuntime providerRuntime, IProviderConfiguration config)
        {
            Name = name;
            OrleansScheduler = TaskScheduler.Current;
            return Task.CompletedTask;
        }

        public Task Close()
        {
            return Task.CompletedTask;
        }
    }
}
