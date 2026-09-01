using AngryMonkey.CloudLogin.Server.Versioning;
using AngryMonkey.CloudLogin.Server.Versioning.V1;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests.V1;

/// <summary>
/// The V1 contract-test location.
/// <para>
/// The legacy V1 contract has not been supplied yet, so the only testable V1 behavior today is
/// the guarantee that enabling it without an implementation fails loudly. When the contract
/// arrives, its compatibility snapshots belong in this file: request/response shapes, routes,
/// status codes, and redirects, pinned the same way <c>V2ContractSnapshotTests</c> pins V2 —
/// against the real captured contract, never an invented one. See
/// <c>docs/api-versioning.md</c> for the extension procedure.
/// </para>
/// </summary>
public class V1ContractTests
{
    [Fact]
    public void V1_IsDefinedButNotImplemented()
    {
        // The version exists in the enum (so configuration can name it) while the adapter
        // surface is deliberately empty until the real contract is provided.
        Assert.Equal(1, (int)CloudLoginApiVersion.V1);
        Assert.Single(typeof(ICloudLoginV1Adapter).GetMethods());
    }

    [Fact]
    public void V1_EnabledWithoutContract_FailsWithActionableMessage()
    {
        CloudLoginV1NotImplementedException exception = Assert.Throws<CloudLoginV1NotImplementedException>(
            () => new ServiceCollection().EnsureVersion1Implemented(CloudLoginApiVersion.V1));

        Assert.Contains("AddCloudLoginV1", exception.Message);
    }

    [Fact]
    public void V1_HasNoControllersYet()
    {
        // A V1 controller could only be built by guessing at the contract. Until the real one
        // arrives there must be nothing to guess from — an invented endpoint that half-works is
        // worse than a version that plainly refuses to start.
        Assert.DoesNotContain(typeof(ICloudLoginV1Adapter).Assembly.GetTypes(), type =>
            type.Namespace?.Contains(".V1", StringComparison.Ordinal) == true
            && type.Name.EndsWith("Controller", StringComparison.Ordinal));
    }

    [Fact]
    public void V1_AdapterSurface_ExposesNoStorageOrCredentialTypes()
    {
        // The adapter translates shapes onto the shared application core; it never hands a caller
        // a persistence document, a hash, or a provider subject. Pinning this now is what stops
        // the future V1 implementation from taking the shortcut of returning storage types.
        Type[] surfaceTypes =
        [
            .. typeof(ICloudLoginV1Adapter).GetMethods()
                .SelectMany(method => method.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType))
        ];

        Assert.DoesNotContain(surfaceTypes, type =>
            type.Namespace?.Contains("Core.Domain", StringComparison.Ordinal) == true
            || type.Name.Contains("Document", StringComparison.Ordinal)
            || type.Name.Contains("Credential", StringComparison.Ordinal));
    }

    [Fact]
    public void V1_SharesTheSameStorageAxis_RatherThanOwningOne()
    {
        // There is no database V1: every API version reads and writes the one core. A V1
        // deployment picking its own store would mean two user databases and a synchronization
        // bridge between them — the failure this whole versioning split exists to avoid.
        Assert.DoesNotContain(Enum.GetNames<CloudLoginDatabaseVersion>(), name => name == "V1");
    }
}
