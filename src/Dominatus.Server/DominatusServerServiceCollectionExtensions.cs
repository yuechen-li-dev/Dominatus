using Dominatus.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Dominatus.Server;

public static class DominatusServerServiceCollectionExtensions
{
    public static IServiceCollection AddDominatusServer(this IServiceCollection services, DominatusServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runtime);

        services.AddSingleton(runtime);
        return services;
    }

    public static IServiceCollection AddDominatusServer(this IServiceCollection services, AiWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return services.AddDominatusServer(new DominatusServerRuntime(world));
    }
}
