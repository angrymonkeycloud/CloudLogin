namespace AngryMonkey.CloudLogin.Server.Versioning;

/// <summary>
/// The API façade versions one CloudLogin deployment can expose. Independent of the
/// deployment/authority version and of the storage <c>SchemaVersion</c>: enabling or disabling a
/// façade never changes what is stored, and migrating storage never renumbers an API.
/// </summary>
public enum CloudLoginApiVersion
{
    /// <summary>
    /// The legacy contract, to be supplied later. Only adapter interfaces and the registration
    /// point exist; enabling V1 without a registered adapter fails startup clearly.
    /// </summary>
    V1 = 1,

    /// <summary>The current working API — routes, JSON names, status codes, redirects, and cookies preserved.</summary>
    V2 = 2,

    /// <summary>The modern API under <c>/api/v3</c> with explicit request/response DTOs.</summary>
    V3 = 3
}

/// <summary>
/// Which API façades a deployment/domain serves. Every façade routes into the same application
/// and storage core — versions are presentation, never data.
/// </summary>
