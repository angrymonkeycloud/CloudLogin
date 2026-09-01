using System.Reflection;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Versioning;
using AngryMonkey.CloudLogin.Server.Versioning.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Tests.Core;

public class ApiVersioningTests
{
    [Fact]
    public void Defaults_AreV3_ForBothVersionAxes()
    {
        // Configure nothing and you get the newest of each, independently: the V3 API façade over
        // the V3 storage model.
        CloudLoginWebConfiguration configuration = new();

        Assert.Equal(CloudLoginApiVersion.V3, configuration.ApiVersion);
        Assert.Equal(CloudLoginDatabaseVersion.V3, configuration.DatabaseVersion);
        Assert.True(configuration.UsesCoreDatabase);
    }

    [Fact]
    public void VersionAxes_AreIndependent()
    {
        // An old API contract over the new storage, and the reverse, are both expressible: the
        // API version chooses what a caller sees, the database version what is written.
        CloudLoginWebConfiguration modernStorageLegacyApi = new()
        {
            ApiVersion = CloudLoginApiVersion.V2,
            DatabaseVersion = CloudLoginDatabaseVersion.V3
        };

        Assert.True(modernStorageLegacyApi.UsesCoreDatabase);
        Assert.Equal(CloudLoginApiVersion.V2, modernStorageLegacyApi.ApiVersion);

        CloudLoginWebConfiguration legacyStorageModernApi = new()
        {
            ApiVersion = CloudLoginApiVersion.V3,
            DatabaseVersion = CloudLoginDatabaseVersion.V2
        };

        Assert.False(legacyStorageModernApi.UsesCoreDatabase);
        Assert.Equal(CloudLoginApiVersion.V3, legacyStorageModernApi.ApiVersion);
    }

    [Fact]
    public void InvalidEnumValue_FailsValidation()
    {
        CloudLoginWebConfiguration configuration = new()
        {
            ApiVersion = (CloudLoginApiVersion)999
        };

        Assert.Throws<InvalidOperationException>(
            () => CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true));
    }

    [Fact]
    public void SelectingV1_WithoutAdapter_FailsStartupClearly()
    {
        CloudLoginV1NotImplementedException exception =
            Assert.Throws<CloudLoginV1NotImplementedException>(
                () => new ServiceCollection().EnsureVersion1Implemented(CloudLoginApiVersion.V1));

        Assert.Contains("ICloudLoginV1Adapter", exception.Message);
    }

    private sealed class FakeV1Adapter : ICloudLoginV1Adapter
    {
        public void MapVersion1(IServiceCollection services) { }
    }

    [Fact]
    public void SelectingV1_WithAdapter_Passes()
    {
        ServiceCollection services = new();
        services.AddCloudLoginV1<FakeV1Adapter>();
        services.EnsureVersion1Implemented(CloudLoginApiVersion.V1);
    }

    private static ResourceExecutingContext BuildContext(CloudLoginApiVersion version)
    {
        CloudLoginWebConfiguration configuration = new() { ApiVersion = version };
        DefaultHttpContext httpContext = new()
        {
            RequestServices = new ServiceCollection().AddSingleton(configuration).BuildServiceProvider()
        };
        return new ResourceExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), [], []);
    }

    /// <summary>
    /// The authority's own endpoints — the sign-in UI's flows and the trusted backend channel —
    /// are version-neutral and must never carry <see cref="ApiVersionGateAttribute"/>.
    /// </summary>
    /// <remarks>
    /// The gate matches the configured version exactly, so a controller pinned to an older version
    /// disappears the moment the default moves on. That is correct for a versioned public façade
    /// and wrong for these: they have no per-version variants, and callers address them by a fixed
    /// unversioned route. Gating <c>ServiceController</c> to V2 made every
    /// <c>CloudLogin/Service/*</c> route 404 once V3 became the default, which surfaced in the
    /// consuming component as missing data rather than as a version problem.
    /// </remarks>
    [Theory]
    [InlineData("ServiceController")]
    [InlineData("ProvidersController")]
    [InlineData("UserController")]
    [InlineData("SecurityController")]
    [InlineData("AccountController")]
    [InlineData("RequestController")]
    [InlineData("AppController")]
    [InlineData("LoginController")]
    [InlineData("AngryMonkey.CloudLogin.Server.Controllers.TokenController")]
    public void AuthorityOwnEndpoints_AreNotVersionGated(string controllerName)
    {
        // Anchored on types from each controllers assembly so both are loaded and the names resolve.
        string fullName = controllerName.Contains('.', StringComparison.Ordinal)
            ? controllerName
            : $"AngryMonkey.CloudLogin.API.Controllers.{controllerName}";

        Type controller =
            typeof(AngryMonkey.CloudLogin.API.Controllers.ServiceController).Assembly.GetType(fullName)
            ?? typeof(CloudLoginServer).Assembly.GetType(fullName)
            ?? throw new InvalidOperationException($"{controllerName} was not found.");

        ApiVersionGateAttribute? gate = controller
            .GetCustomAttributes(typeof(ApiVersionGateAttribute), inherit: true)
            .Cast<ApiVersionGateAttribute>()
            .FirstOrDefault();

        Assert.True(
            gate is null,
            $"{controllerName} carries [ApiVersionGate({gate?.Version})]. The authority's own endpoints are " +
            "version-neutral; gating one hides it entirely as soon as the configured API version differs.");
    }

    [Fact]
    public void Gate_AllowsOnlySelectedVersion()
    {
        ResourceExecutingContext rejected = BuildContext(CloudLoginApiVersion.V3);
        new ApiVersionGateAttribute(CloudLoginApiVersion.V2).OnResourceExecuting(rejected);
        Assert.IsType<NotFoundResult>(rejected.Result);

        ResourceExecutingContext accepted = BuildContext(CloudLoginApiVersion.V2);
        new ApiVersionGateAttribute(CloudLoginApiVersion.V2).OnResourceExecuting(accepted);
        Assert.Null(accepted.Result);
    }

    [Fact]
    public void SelectedV3_AlsoGetsUnversionedAlias()
    {
        Microsoft.AspNetCore.Mvc.ApplicationModels.ControllerModel controller = new(
            typeof(ApiVersioningTests).GetTypeInfo(),
            [new ApiVersionGateAttribute(CloudLoginApiVersion.V3)]);
        controller.Selectors.Add(new Microsoft.AspNetCore.Mvc.ApplicationModels.SelectorModel
        {
            AttributeRouteModel = new Microsoft.AspNetCore.Mvc.ApplicationModels.AttributeRouteModel
            {
                Template = "api/v3/users"
            }
        });

        new SelectedApiVersionRouteConvention(CloudLoginApiVersion.V3).Apply(controller);

        Assert.Contains(controller.Selectors, selector =>
            selector.AttributeRouteModel?.Template == "api/users");
    }

    [Fact]
    public void ApiVersion_DoesNotChangeSchemaOrRealm()
    {
        CloudLoginWebConfiguration configuration = new()
        {
            ApiVersion = CloudLoginApiVersion.V3,
            Core = new AngryMonkey.CloudLogin.Server.Core.CloudLoginCoreConfiguration
            {
                RealmId = "tenant-a"
            }
        };

        Assert.Equal("tenant-a", configuration.Core.RealmId);
        Assert.Equal(1, AngryMonkey.CloudLogin.Server.Core.Domain.CloudLoginCoreSchema.CurrentVersion);
    }
}
