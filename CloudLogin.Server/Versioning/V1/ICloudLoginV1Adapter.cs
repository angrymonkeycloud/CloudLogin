using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AngryMonkey.CloudLogin.Server.Versioning.V1;

/// <summary>
/// The V1 façade adapter. The legacy V1 contract will be supplied later; until then this
/// interface is deliberately minimal — it defines where a V1 implementation plugs in, not what
/// V1 looks like. Do not invent V1 shapes here: extend this interface only from the real
/// contract when it arrives (see <c>docs/api-versioning.md</c> for the extension procedure).
/// </summary>
public interface ICloudLoginV1Adapter
{
    /// <summary>
    /// Registers the V1 endpoints (controllers, routes, and their compatibility translation)
    /// against the shared application core. Called once at startup when V1 is enabled.
    /// </summary>
    void MapVersion1(IServiceCollection services);
}

/// <summary>Thrown when V1 is enabled but no adapter implementation has been registered.</summary>
public sealed class CloudLoginV1NotImplementedException() : InvalidOperationException(
    "API version V1 is selected, but no ICloudLoginV1Adapter is registered. " +
    "The V1 legacy contract has not been supplied yet: select V2/V3, or register an " +
    "adapter via AddCloudLoginV1(...) once the contract exists. V1 must never be silently stubbed.");

public static class CloudLoginV1ServiceCollectionExtensions
{
    /// <summary>
    /// The single registration point for the future V1 façade. The adapter translates V1's
    /// contract onto the same application and storage core every other version uses — a V1
    /// deployment never gets its own user database or a synchronization bridge.
    /// </summary>
    public static IServiceCollection AddCloudLoginV1<TAdapter>(this IServiceCollection services)
        where TAdapter : class, ICloudLoginV1Adapter, new()
    {
        TAdapter adapter = new();
        services.TryAddSingleton<ICloudLoginV1Adapter>(adapter);
        adapter.MapVersion1(services);
        return services;
    }

    /// <summary>
    /// Fails startup clearly when V1 is enabled without an implementation. Called by the web
    /// registration path after all services are wired.
    /// </summary>
    public static void EnsureVersion1Implemented(this IServiceCollection services, CloudLoginApiVersion apiVersion)
    {
        if (apiVersion != CloudLoginApiVersion.V1)
            return;

        bool adapterRegistered = services.Any(descriptor => descriptor.ServiceType == typeof(ICloudLoginV1Adapter));

        if (!adapterRegistered)
            throw new CloudLoginV1NotImplementedException();
    }
}
