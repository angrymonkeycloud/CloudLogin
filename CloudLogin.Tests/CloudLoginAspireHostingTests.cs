using AngryMonkey.CloudLogin.Aspire.Hosting;
using AngryMonkey.CloudLogin.Server;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace AngryMonkey.CloudLogin.Tests;

public sealed class CloudLoginAspireHostingTests
{
    [Fact]
    public async Task ProjectlessCloudLogin_WiresAConsumerWithoutManualConfiguration()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions
            {
                Args = [],
                DisableDashboard = true
            });

        IResourceBuilder<ProjectResource> login = builder.AddCloudLogin();
        IResourceBuilder<ExecutableResource> consumer = builder
            .AddExecutable("api", "dotnet", ".")
            .WithHttpEndpoint()
            .WithReference(login);

        Assert.True(File.Exists(login.Resource.GetProjectMetadata().ProjectPath));

        // Nothing is provisioned for a server that was never pointed at an account. Adding
        // CloudLogin declares a project; where its data lives is a separate, explicit decision.
        Assert.Empty(builder.Resources.OfType<AzureCosmosDBResource>());
        Assert.Empty(builder.Resources.OfType<AzureStorageResource>());

        Dictionary<string, object> consumerEnvironment =
            await ReadEnvironmentAsync(consumer.Resource);
        Dictionary<string, object> loginEnvironment =
            await ReadEnvironmentAsync(login.Resource);

        Assert.Contains("LoginUrl", consumerEnvironment);
        Assert.Contains("CloudLogin:Authority", consumerEnvironment);
        Assert.Equal("api", consumerEnvironment["CloudLogin:Audience"]);
        Assert.Equal("api", consumerEnvironment["CloudLogin:ClientId"]);
        Assert.Contains("CloudLogin:ClientSecret", consumerEnvironment);

        Assert.Equal("true", loginEnvironment["TestMode:IsEnabled"]);
        Assert.Equal("false", loginEnvironment["CloudLogin:Security:RequireHttps"]);
        Assert.Equal("Development", loginEnvironment["DOTNET_ENVIRONMENT"]);
        Assert.Equal("Development", loginEnvironment["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("api", loginEnvironment["CloudLoginTokens:AllowedAudiences:0"]);
        Assert.Contains("CloudLoginTokens:ServiceClients:api:ClientSecret", loginEnvironment);
        Assert.Contains("CloudLogin:AllowedRedirectOrigins:0", loginEnvironment);
        Assert.Single(login.Resource.Annotations.OfType<CloudLoginConsumerAnnotation>());
    }

    [Fact]
    public void CloudLoginDatabaseConfiguration_UpdatesProvisionedCosmosResources()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions
            {
                Args = ["--operation", "publish"],
                DisableDashboard = true
            });

        ICloudLoginServerBuilder login = builder.AddCloudLogin("login", configuration =>
        {
            configuration.Cosmos.DatabaseId = "Accounts";
            configuration.Cosmos.ContainerId = "Users";
        });

        login.WithReference(builder.AddAzureCosmosDB("cosmos"));

        AzureCosmosDBDatabaseResource database = Assert.Single(builder.Resources.OfType<AzureCosmosDBDatabaseResource>());
        AzureCosmosDBContainerResource container = Assert.Single(builder.Resources.OfType<AzureCosmosDBContainerResource>());

        Assert.Equal("Accounts", database.DatabaseName);
        Assert.Equal("Users", container.ContainerName);
        Assert.Equal("/pk", container.PartitionKeyPath);
    }

    [Fact]
    public async Task ReferencesChainInAnyOrder_WithoutFallingBackToAspiresGenericOverload()
    {
        // The AppHost style: every reference under the previous one. Each of this package's
        // overloads returns the caller's own builder type rather than IResourceBuilder<T>, so the
        // next call in the chain still finds them. Without that, WithReference(cosmos) binds to
        // Aspire's generic overload instead - which compiles, sets only ConnectionStrings__cosmos,
        // and leaves CloudLogin reading nothing.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder authority = builder.AddCloudLogin("authority");
        ICloudLoginServerBuilder login = builder.AddCloudLogin("login");

        login
            .WithReference(authority)
            .WithReference(builder.AddAzureCosmosDB("cosmos").RunAsEmulator())
            .WithReference(builder.AddAzureStorage("storage").RunAsEmulator());

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);

        // The Cosmos overload is the one that can silently fall through, because Aspire does have a
        // generic overload accepting it. This key exists only if CloudLogin's own ran.
        Assert.Contains("Cosmos:ConnectionString", environment);
        Assert.Contains("Cosmos:DatabaseId", environment);
        Assert.Contains("ConnectionStrings__cosmos", environment);
    }

    [Fact]
    public async Task ServiceAccess_ConfiguresBothEndsOfTheBackendChannel()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin();
        IResourceBuilder<ExecutableResource> portal = builder
            .AddExecutable("portal", "dotnet", ".")
            .WithHttpEndpoint()
            .WithReference(login)
            .WithServiceAccess(login);

        Dictionary<string, object> portalEnvironment = await ReadEnvironmentAsync(portal.Resource);
        Dictionary<string, object> loginEnvironment = await ReadEnvironmentAsync(login.Resource);

        Assert.Contains("CloudLogin:BaseUrl", portalEnvironment);
        Assert.Contains("CloudLogin:ServiceKey", portalEnvironment);

        // Indexed, because CloudLoginWebConfiguration.ServiceKeys is a List<string>. A singular
        // 'CloudLogin:ServiceKey' on the authority binds to nothing, leaving ServiceKeys empty and
        // ServiceKeyAuthenticationHandler rejecting every call for want of a configured key.
        Assert.Contains("CloudLogin:ServiceKeys:0", loginEnvironment);
        Assert.DoesNotContain("CloudLogin:ServiceKey", loginEnvironment.Keys);
    }

    [Fact]
    public async Task WithServiceAccess_NeedsNoPriorWithReference()
    {
        // The two are independent: a pure backend reader that never signs a user in still needs
        // only the one call.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin();
        IResourceBuilder<ExecutableResource> worker = builder
            .AddExecutable("worker", "dotnet", ".")
            .WithServiceAccess(login);

        Dictionary<string, object> workerEnvironment = await ReadEnvironmentAsync(worker.Resource);

        Assert.Contains("CloudLogin:BaseUrl", workerEnvironment);
        Assert.Contains("CloudLogin:ServiceKey", workerEnvironment);
        Assert.DoesNotContain("CloudLogin:Authority", workerEnvironment.Keys);
    }

    [Fact]
    public async Task WithServiceAccess_CalledTwice_GrantsOnlyOneKey()
    {
        // Idempotent, the way AddConsumer already is for WithReference: a second call from the same
        // caller must not add a second parameter under the same generated name (Aspire rejects a
        // duplicate resource name) or advance the authority's index a second time.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin();
        IResourceBuilder<ExecutableResource> worker = builder.AddExecutable("worker", "dotnet", ".");

        worker.WithServiceAccess(login);
        worker.WithServiceAccess(login);

        Dictionary<string, object> loginEnvironment = await ReadEnvironmentAsync(login.Resource);

        Assert.Contains("CloudLogin:ServiceKeys:0", loginEnvironment);
        Assert.DoesNotContain("CloudLogin:ServiceKeys:1", loginEnvironment.Keys);
    }

    [Fact]
    public async Task APlainReference_GetsNoServiceKey()
    {
        // The key bypasses user identity on CloudLogin/Service/*, so signing users in must not be
        // enough to be handed one.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin();
        IResourceBuilder<ExecutableResource> app = builder
            .AddExecutable("app", "dotnet", ".")
            .WithHttpEndpoint()
            .WithReference(login);

        Dictionary<string, object> appEnvironment = await ReadEnvironmentAsync(app.Resource);
        Dictionary<string, object> loginEnvironment = await ReadEnvironmentAsync(login.Resource);

        // Still a full sign-in consumer.
        Assert.Contains("CloudLogin:Authority", appEnvironment);

        Assert.DoesNotContain("CloudLogin:ServiceKey", appEnvironment.Keys);
        Assert.DoesNotContain("CloudLogin:BaseUrl", appEnvironment.Keys);
        Assert.DoesNotContain(loginEnvironment.Keys, key => key.StartsWith("CloudLogin:ServiceKeys", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithReference_MapsCosmosOntoCloudLoginsOwnKeys_AndKeepsAspiresConnectionString()
    {
        // The two halves of the contract: an ordinary Aspire reference still happens (a host that
        // reads ConnectionStrings:cosmos keeps working), and the same call additionally writes the
        // configuration keys the CloudLogin server actually binds.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin();

        login.WithReference(builder.AddAzureCosmosDB("cosmos").RunAsEmulator());

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);

        Assert.Contains("ConnectionStrings__cosmos", environment);
        Assert.Contains("Cosmos:ConnectionString", environment);
        Assert.Equal("Users", environment["Cosmos:DatabaseId"]);
        Assert.Equal("Data", environment["Cosmos:ContainerId"]);

        // The Linux Cosmos emulator speaks Gateway mode only, and a local run is the only thing
        // that uses one.
        Assert.Equal("true", environment["Cosmos:GatewayMode"]);
    }

    [Fact]
    public async Task WithReference_MapsStorageOntoOneConnectionStringCoveringEveryService()
    {
        // CloudLogin reaches blobs and tables through a single connection string, so the account is
        // referenced rather than one of its children: a blob-scoped string hands the table SDK an
        // endpoint it cannot authenticate against. Locally that string is assembled from the
        // endpoints Azurite is actually listening on, which is why a host pairs this with WaitFor.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin();
        IResourceBuilder<AzureStorageResource> storage = builder.AddAzureStorage("storage").RunAsEmulator();

        login.WithReference(storage);

        // Stands in for DCP allocating the emulator's endpoints once its container is running.
        foreach (EndpointAnnotation endpoint in storage.Resource.Annotations.OfType<EndpointAnnotation>())
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", 60000 + endpoint.Name.Length);

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);

        string connectionString = Assert.IsType<string>(environment["Storage:ConnectionString"]);

        Assert.Contains("AccountName=devstoreaccount1", connectionString);
        Assert.Contains("BlobEndpoint=http://127.0.0.1:", connectionString);
        Assert.Contains("TableEndpoint=", connectionString);
        Assert.Contains("QueueEndpoint=", connectionString);

        // The account path must survive: the Azure SDK drops it from a 'localhost' endpoint, which
        // is what makes Azurite answer 400.
        Azure.Storage.Blobs.BlobContainerClient client = new(connectionString, "files");
        Assert.Contains("/devstoreaccount1/files", client.Uri.AbsoluteUri);
    }


    [Fact]
    public async Task PublishReference_KeepsAuthenticationWiringWithoutLocalServiceDiscovery()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions
            {
                Args = ["--operation", "publish"],
                DisableDashboard = true
            });

        IResourceBuilder<ProjectResource> login = builder.AddCloudLogin();
        IResourceBuilder<ExecutableResource> consumer = builder
            .AddExecutable("api", "dotnet", ".")
            .WithHttpEndpoint()
            .WithReference(login);


        Dictionary<string, object> consumerEnvironment = await ReadEnvironmentAsync(
            consumer.Resource,
            DistributedApplicationOperation.Publish);
        Dictionary<string, object> loginEnvironment = await ReadEnvironmentAsync(
            login.Resource,
            DistributedApplicationOperation.Publish);

        Assert.Contains("CloudLogin:Authority", consumerEnvironment);
        Assert.Equal("api", consumerEnvironment["CloudLogin:Audience"]);
        Assert.Equal("api", loginEnvironment["CloudLoginTokens:AllowedAudiences:0"]);
        Assert.DoesNotContain("TestMode:IsEnabled", loginEnvironment);
        Assert.DoesNotContain(
            consumerEnvironment.Keys,
            key => key.StartsWith("services__", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Dictionary<string, object>> ReadEnvironmentAsync(
        IResourceWithEnvironment resource,
        DistributedApplicationOperation operation = DistributedApplicationOperation.Run)
    {
        Dictionary<string, object> environment = [];
        DistributedApplicationExecutionContext executionContext =
            new(operation);

        foreach (EnvironmentCallbackAnnotation annotation in
                 resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            EnvironmentCallbackContext context = new(
                executionContext,
                (IResource)resource,
                environment,
                CancellationToken.None);

            await annotation.Callback(context);
        }

        return environment;
    }
}
