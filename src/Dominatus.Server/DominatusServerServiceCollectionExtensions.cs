using Dominatus.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Dominatus.Server;

public static class DominatusServerServiceCollectionExtensions
{
    public static IServiceCollection AddDominatusServer(this IServiceCollection services, DominatusServerRuntime runtime, DominatusLlmStreamRegistry? streamRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runtime);

        services.AddSingleton(runtime);
        services.AddSingleton(streamRegistry ?? new DominatusLlmStreamRegistry());
        return services;
    }

    public static IServiceCollection AddDominatusServer(this IServiceCollection services, AiWorld world, DominatusLlmStreamRegistry? streamRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        return services.AddDominatusServer(new DominatusServerRuntime(world), streamRegistry);
    }
}
