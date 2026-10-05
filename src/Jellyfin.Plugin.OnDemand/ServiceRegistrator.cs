using Jellyfin.Plugin.OnDemand.State;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.OnDemand;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton(sp =>
            new StateStore(Path.Combine(sp.GetRequiredService<IApplicationPaths>().DataPath, "ondemand", "state.json")));
        services.AddHttpClient();
    }
}
