using AngryMonkey.CloudLogin.Aspire.Hosting;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>What <c>project.WithReference(cloudLogin)</c> writes for the application and for the authority, in run and publish modes.</summary>
public sealed class CloudLoginClientReferenceTests
{
    private const string SecretKeyZero = "CloudLoginTokens:SecretKeys:0";

    private static IDistributedApplicationBuilder NewBuilder(bool publish = false) =>
        DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = publish ? ["--operation", "publish"] : [], DisableDashboard = true });

    private static DistributedApplicationOperation Operation(bool publish) => publish ? DistributedApplicationOperation.Publish : DistributedApplicationOperation.Run;

    private static async Task<Dictionary<string, object>> EnvironmentOfAsync(IResourceWithEnvironment resource, bool publish = false)
    {
        Dictionary<string, object> environment = [];
        DistributedApplicationExecutionContext executionContext = new(Operation(publish));

        foreach (EnvironmentCallbackAnnotation annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await annotation.Callback(new EnvironmentCallbackContext(executionContext, (IResource)resource, environment, CancellationToken.None));

        return environment;
    }

    private static async Task<string> ValueOfAsync(object value) => await Assert.IsType<ParameterResource>(value).GetValueAsync(default) ?? string.Empty;

    private static IResourceBuilder<ExecutableResource> Shop(IDistributedApplicationBuilder builder, string name = "shop") =>
        builder.AddExecutable(name, "dotnet", ".").WithHttpEndpoint();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APlainReference_GivesTheApplicationItsNameAddressAndKey_AndRegistersNothingAboutIt(bool publish)
    {
        IDistributedApplicationBuilder builder = NewBuilder(publish);
        ICloudLoginServerBuilder login = builder.AddCloudLoginProject();
        IResourceBuilder<ExecutableResource> shop = Shop(builder).WithReference(login);

        Dictionary<string, object> application = await EnvironmentOfAsync(shop.Resource, publish);
        Dictionary<string, object> authority = await EnvironmentOfAsync(login.Resource, publish);

        Assert.Equal("shop", application["CloudLogin:ClientId"]);
        Assert.Equal("shop", application["CloudLogin:Audience"]);
        Assert.IsType<EndpointReference>(application["CloudLogin:PublicUrl"]);
        Assert.Contains(SecretKeyZero, authority);
        Assert.DoesNotContain(authority.Keys, key => key.Contains("shop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(authority.Keys, key => key.Contains("ServiceClients", StringComparison.Ordinal) || key.Contains("AllowedAudiences", StringComparison.Ordinal));

        foreach (string key in application.Keys.Concat(authority.Keys))
        {
            Assert.DoesNotContain("Origin", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cors", key, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task EveryWebsite_SharesTheAppHostsOneKey_DeclaredToTheAuthorityOnce()
    {
        IDistributedApplicationBuilder builder = NewBuilder();
        ICloudLoginServerBuilder login = builder.AddCloudLoginProject();
        IResourceBuilder<ExecutableResource> first = Shop(builder, "shop").WithReference(login);
        IResourceBuilder<ExecutableResource> second = Shop(builder, "blog").WithReference(login);

        Dictionary<string, object> authority = await EnvironmentOfAsync(login.Resource);
        Dictionary<string, object> firstEnvironment = await EnvironmentOfAsync(first.Resource);
        Dictionary<string, object> secondEnvironment = await EnvironmentOfAsync(second.Resource);

        Assert.Same(authority[SecretKeyZero], firstEnvironment["CloudLogin:ClientSecret"]);
        Assert.Same(authority[SecretKeyZero], secondEnvironment["CloudLogin:ClientSecret"]);
        Assert.Single(authority.Keys, key => key.StartsWith("CloudLoginTokens:SecretKeys", StringComparison.Ordinal));
        Assert.True((await ValueOfAsync(authority[SecretKeyZero])).Length >= 32);
    }

    [Fact]
    public async Task APublicClient_HasNoSecretAnywhere_ButKnowsItsOwnAddress()
    {
        IDistributedApplicationBuilder builder = NewBuilder();
        ICloudLoginServerBuilder login = builder.AddCloudLoginProject();
        IResourceBuilder<ExecutableResource> spa = Shop(builder, "spa").WithReference(login, options => options.IsPublic = true);

        Dictionary<string, object> application = await EnvironmentOfAsync(spa.Resource);
        Dictionary<string, object> authority = await EnvironmentOfAsync(login.Resource);

        Assert.DoesNotContain(application.Keys, key => key.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(authority.Keys, key => key.StartsWith("CloudLoginTokens:SecretKeys", StringComparison.Ordinal));
        Assert.IsType<EndpointReference>(application["CloudLogin:PublicUrl"]);
        Assert.Equal("spa", application["CloudLogin:ClientId"]);
    }

    [Fact]
    public async Task APublicClient_WithNowhereToReturnTo_IsRefusedWhenTheModelIsEvaluated()
    {
        IDistributedApplicationBuilder builder = NewBuilder();
        ICloudLoginServerBuilder login = builder.AddCloudLoginProject();
        IResourceBuilder<ExecutableResource> worker = builder.AddExecutable("worker", "dotnet", ".").WithReference(login, options => options.IsPublic = true);

        await Assert.ThrowsAsync<DistributedApplicationException>(() => EnvironmentOfAsync(worker.Resource));
    }

    [Fact]
    public async Task ABackendWithNoEndpoint_PublishesNoAddress_ButStillGetsTheKey()
    {
        IDistributedApplicationBuilder builder = NewBuilder();
        ICloudLoginServerBuilder login = builder.AddCloudLoginProject();
        IResourceBuilder<ExecutableResource> worker = builder.AddExecutable("worker", "dotnet", ".").WithReference(login);

        Dictionary<string, object> application = await EnvironmentOfAsync(worker.Resource);

        Assert.DoesNotContain("CloudLogin:PublicUrl", application);
        Assert.Contains("CloudLogin:ClientSecret", application);
        Assert.Equal("worker", application["CloudLogin:ClientId"]);
    }

    [Fact]
    public async Task AnEndpointAddedAfterTheReference_StillProvidesTheAddress()
    {
        IDistributedApplicationBuilder builder = NewBuilder();
        ICloudLoginServerBuilder login = builder.AddCloudLoginProject();
        IResourceBuilder<ExecutableResource> late = builder.AddExecutable("late", "dotnet", ".").WithReference(login).WithHttpEndpoint();

        Dictionary<string, object> application = await EnvironmentOfAsync(late.Resource);

        Assert.IsType<EndpointReference>(application["CloudLogin:PublicUrl"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnExternalAuthority_GivesTheApplicationTheSameRuntimeShapeAsOneInTheAppHost(bool publish)
    {
        IDistributedApplicationBuilder inHost = NewBuilder(publish);
        ICloudLoginServerBuilder owned = inHost.AddCloudLoginProject("login");
        IResourceBuilder<ExecutableResource> local = Shop(inHost).WithReference(owned);

        IDistributedApplicationBuilder remote = NewBuilder(publish);
        ICloudLoginExternalBuilder external = remote.AddCloudLogin("login", "https://login.example.com");
        IResourceBuilder<ExecutableResource> shop = Shop(remote).WithReference(external);

        Dictionary<string, object> localEnvironment = await EnvironmentOfAsync(local.Resource, publish);
        Dictionary<string, object> remoteEnvironment = await EnvironmentOfAsync(shop.Resource, publish);

        string[] shared = ["LoginUrl", "CloudLogin:Authority", "CloudLogin:Audience", "CloudLogin:ClientId", "CloudLogin:ClientSecret", "CloudLogin:PublicUrl"];

        foreach (string key in shared)
        {
            Assert.Contains(key, localEnvironment);
            Assert.Contains(key, remoteEnvironment);
        }

        Assert.Equal(localEnvironment["CloudLogin:ClientId"], remoteEnvironment["CloudLogin:ClientId"]);
        Assert.Equal(localEnvironment["CloudLogin:Audience"], remoteEnvironment["CloudLogin:Audience"]);
        Assert.IsType<EndpointReference>(remoteEnvironment["CloudLogin:PublicUrl"]);
        Assert.True(Assert.IsType<ParameterResource>(remoteEnvironment["CloudLogin:ClientSecret"]).Secret);
    }

    [Fact]
    public async Task AnExternalAuthority_AsksForOneKeyForEveryWebsite_AndNoneForAPublicClient()
    {
        IDistributedApplicationBuilder builder = NewBuilder();
        ICloudLoginExternalBuilder external = builder.AddCloudLogin("login", "https://login.example.com");
        IResourceBuilder<ExecutableResource> shop = Shop(builder, "shop").WithReference(external);
        IResourceBuilder<ExecutableResource> blog = Shop(builder, "blog").WithReference(external);
        IResourceBuilder<ExecutableResource> spa = Shop(builder, "spa").WithReference(external, configure: options => options.IsPublic = true);

        Dictionary<string, object> shopEnvironment = await EnvironmentOfAsync(shop.Resource);
        Dictionary<string, object> blogEnvironment = await EnvironmentOfAsync(blog.Resource);
        Dictionary<string, object> spaEnvironment = await EnvironmentOfAsync(spa.Resource);

        ParameterResource key = Assert.IsType<ParameterResource>(shopEnvironment["CloudLogin:ClientSecret"]);
        Assert.Equal("login-secret-key", key.Name);
        Assert.Same(key, blogEnvironment["CloudLogin:ClientSecret"]);
        Assert.DoesNotContain(spaEnvironment.Keys, name => name.Contains("Secret", StringComparison.Ordinal));
        Assert.Empty(external.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>());
    }

    [Fact]
    public async Task AnExternalAuthority_UsesAKeyPassedForOneApplication()
    {
        IDistributedApplicationBuilder builder = NewBuilder();
        ICloudLoginExternalBuilder external = builder.AddCloudLogin("login", "https://login.example.com");
        IResourceBuilder<ParameterResource> partnerKey = builder.AddParameter("partner-key", secret: true);
        IResourceBuilder<ExecutableResource> shop = Shop(builder).WithReference(external, clientSecret: partnerKey);

        Assert.Same(partnerKey.Resource, (await EnvironmentOfAsync(shop.Resource))["CloudLogin:ClientSecret"]);
    }

    [Fact]
    public async Task APublishedManifest_NeverCarriesTheKeyValue()
    {
        IDistributedApplicationBuilder builder = NewBuilder(publish: true);
        ICloudLoginServerBuilder login = builder.AddCloudLoginProject();
        IResourceBuilder<ExecutableResource> shop = Shop(builder, "shop").WithReference(login);

        Dictionary<string, object> authority = await EnvironmentOfAsync(login.Resource, publish: true);
        Dictionary<string, object> shopEnvironment = await EnvironmentOfAsync(shop.Resource, publish: true);

        foreach (object secret in new[] { shopEnvironment["CloudLogin:ClientSecret"], authority[SecretKeyZero] })
        {
            ParameterResource parameter = Assert.IsType<ParameterResource>(secret);
            string value = await ValueOfAsync(parameter);

            Assert.True(parameter.Secret);
            Assert.False(string.IsNullOrEmpty(value));
            Assert.DoesNotContain(value, parameter.ValueExpression, StringComparison.Ordinal);
            Assert.StartsWith("{", parameter.ValueExpression);
        }
    }
}
