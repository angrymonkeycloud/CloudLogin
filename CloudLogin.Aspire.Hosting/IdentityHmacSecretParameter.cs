using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.Security.Cryptography;

namespace AngryMonkey.CloudLogin.Aspire.Hosting;

/// <summary>
/// Supplies the secret CloudLogin keys its V3 identity index with, as an Aspire parameter the
/// AppHost generates once and then keeps.
/// </summary>
/// <remarks>
/// <para>
/// CloudLogin itself never generates this: the secret <em>is</em> the index, so a value that
/// differed between two starts would resolve none of the rows written under the previous one, and
/// the server refuses to start rather than silently orphan every account. That leaves the AppHost
/// as the right place to mint one — it is the only component that outlives a single process and
/// has somewhere durable to put it.
/// </para>
/// <para>
/// Stability comes from Aspire's own parameter machinery rather than anything invented here.
/// Locally, <c>persist: true</c> writes the generated value into the AppHost's user secrets on
/// first run and reads it back on every run afterwards. When published, the value is a parameter
/// the deployment resolves once per environment and reuses across republishes, slots and scaled
/// instances — so every replica of one deployment keys the index identically.
/// </para>
/// </remarks>
internal static class IdentityHmacSecretParameter
{
    /// <summary>
    /// The parameter name, derived from the CloudLogin resource so two authorities in one AppHost
    /// get one secret each rather than sharing — separate authorities are separate identity
    /// indexes.
    /// </summary>
    internal static string NameFor(string resourceName) => $"{resourceName}-identity-hmac";

    /// <summary>
    /// Adds (or reuses) the generated secret parameter for a CloudLogin resource.
    /// </summary>
    internal static IResourceBuilder<ParameterResource> AddFor(
        IDistributedApplicationBuilder builder, string resourceName)
    {
        string parameterName = NameFor(resourceName);

        // Reuse an already-declared parameter of the same name, so an AppHost that declared its
        // own before calling AddCloudLogin keeps it rather than getting a second, conflicting one.
        IResourceBuilder<ParameterResource>? existing = builder.Resources
            .OfType<ParameterResource>()
            .FirstOrDefault(parameter => string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
            is { } resource
            ? builder.CreateResourceBuilder(resource)
            : null;

        return existing ?? builder.AddParameter(
            parameterName,
            new IdentityHmacSecretDefault(builder.ExecutionContext.IsRunMode ? builder.AppHostDirectory : null, parameterName),
            secret: true,
            persist: false);
    }
}

/// <summary>
/// Generates the identity secret: base64 of 32 cryptographically random bytes, which is exactly
/// what <c>CloudLogin:IdentityHmacSecret</c> expects.
/// </summary>
/// <remarks>
/// <para>
/// The manifest form is delegated to a <see cref="GenerateParameterDefault"/> (which is sealed, so
/// this composes rather than inherits). A published manifest must never carry the literal secret,
/// so what it emits instead is a <em>description</em> of how to generate one, which the deployment
/// resolves once per environment and then reuses across republishes, slots and instances.
/// </para>
/// <para>
/// The delegated settings are chosen so a deployment-generated value passes the same validation as
/// a locally generated one. Aspire's generator draws from 56 characters when special characters
/// are off, all of which are inside the base64 alphabet, so 44 of them form a valid base64 string
/// decoding to 33 bytes — past the 32-byte floor, with far more entropy than the floor requires.
/// Special characters are excluded because they would break that decoding, and because they are
/// the ones that need escaping in an App Service setting.
/// </para>
/// </remarks>
internal sealed class IdentityHmacSecretDefault(string? appHostDirectory = null, string name = "identity-hmac") : ParameterDefault
{
    /// <summary>Base64 of 32 bytes is 44 characters; asking for 44 keeps both forms the same length.</summary>
    private const int Base64LengthOf32Bytes = 44;

    private static readonly GenerateParameterDefault ManifestForm = new()
    {
        MinLength = Base64LengthOf32Bytes,
        Lower = true,
        Upper = true,
        Numeric = true,
        Special = false
    };

    /// <summary>
    /// The locally generated value: base64 of <c>RandomNumberGenerator.GetBytes(32)</c>, taken
    /// straight from the platform CSPRNG rather than assembled character by character.
    /// </summary>
    /// <remarks>
    /// The byte count is the server's own <c>MinimumSecretBytes</c>, so the generator and the
    /// validator that will reject its output cannot drift apart.
    /// </remarks>
    public override string GetDefaultValue() => appHostDirectory is null
        ? Generate()
        : CoconutSharp.Aspire.Hosting.CoconutLocalSecretStore.GetOrCreate(appHostDirectory, name, Generate);

    private static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(
            AngryMonkey.CloudLogin.Server.Core.Domain.IdentityKeyHasher.MinimumSecretBytes));

    /// <summary>Writes the generation descriptor — never the value this instance would produce.</summary>
    public override void WriteToManifest(global::Aspire.Hosting.Publishing.ManifestPublishingContext context) =>
        ManifestForm.WriteToManifest(context);
}
