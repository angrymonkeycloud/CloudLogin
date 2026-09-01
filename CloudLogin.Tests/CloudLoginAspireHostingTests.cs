using AngryMonkey.CloudLogin.Aspire.Hosting;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Versioning;
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
            // The legacy database/container names belong to database version V2.
            configuration.DatabaseVersion = CloudLoginDatabaseVersion.V2;
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
    public void DefaultConfiguration_DeclaresTheCoreContainersInADatabaseNamedLogin()
    {
        // No configuration at all: database version V3 is the default, so a plain reference
        // declares the core schema and nothing legacy.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--operation", "publish"], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin("login");
        login.WithReference(builder.AddAzureCosmosDB("cosmos"));

        AzureCosmosDBDatabaseResource database = Assert.Single(builder.Resources.OfType<AzureCosmosDBDatabaseResource>());
        Assert.Equal(CloudLoginCoreContainers.DefaultDatabaseId, database.DatabaseName);
        Assert.Equal("Login", database.DatabaseName);

        List<AzureCosmosDBContainerResource> containers = [.. builder.Resources.OfType<AzureCosmosDBContainerResource>()];

        Dictionary<string, string> expected = new()
        {
            [CloudLoginCoreContainers.Users] = CloudLoginCoreContainers.UsersPartitionKey,
            [CloudLoginCoreContainers.Credentials] = CloudLoginCoreContainers.CredentialsPartitionKey,
            [CloudLoginCoreContainers.Workspaces] = CloudLoginCoreContainers.WorkspacesPartitionKey,
            [CloudLoginCoreContainers.WorkspaceAccess] = CloudLoginCoreContainers.WorkspaceAccessPartitionKey,
            [CloudLoginCoreContainers.Sessions] = CloudLoginCoreContainers.SessionsPartitionKey,
            [CloudLoginCoreContainers.LoginRequests] = CloudLoginCoreContainers.LoginRequestsPartitionKey,
            [CloudLoginCoreContainers.AuditEvents] = CloudLoginCoreContainers.AuditEventsPartitionKey,

            // Declared even though only a deployment without Key Vault signing reads it. The server
            // cannot create a container itself - that is a control-plane call, and it runs on a
            // data-plane-only managed identity - so leaving this out breaks every relying party's
            // token exchange with a 500 while sign-in at the authority still appears to work.
            [CloudLoginCoreContainers.SigningKeysFallback] = CloudLoginCoreContainers.SigningKeysFallbackPartitionKey
        };

        Assert.Equal(expected.Count, containers.Count);

        foreach ((string name, string partitionKeyPath) in expected)
        {
            AzureCosmosDBContainerResource container = Assert.Single(containers, c => c.ContainerName == name);
            Assert.Equal(partitionKeyPath, container.PartitionKeyPath);
            Assert.Equal(database, container.Parent);
        }
    }

    [Fact]
    public void CoreDatabaseId_MatchesTheRuntimeConfigurationsOwnDefault()
    {
        // The provisioning-time default and the runtime default must be the literal same string,
        // or the AppHost provisions a database the running server never opens.
        Assert.Equal(CloudLoginCoreContainers.DefaultDatabaseId, new CloudLoginCoreConfiguration().DatabaseId);
        Assert.Equal(CloudLoginDatabaseVersion.V3, new CloudLoginWebConfiguration().DatabaseVersion);
    }

    [Fact]
    public void DatabaseVersionV2_DeclaresOnlyTheLegacyDatabaseAndContainer()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--operation", "publish"], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin("login", configuration =>
        {
            configuration.DatabaseVersion = CloudLoginDatabaseVersion.V2;
            configuration.Cosmos.DatabaseId = "Accounts";
            configuration.Cosmos.ContainerId = "Data";
        });

        login.WithReference(builder.AddAzureCosmosDB("cosmos"));

        AzureCosmosDBDatabaseResource database = Assert.Single(builder.Resources.OfType<AzureCosmosDBDatabaseResource>());
        AzureCosmosDBContainerResource container = Assert.Single(builder.Resources.OfType<AzureCosmosDBContainerResource>());

        Assert.Equal("Accounts", database.DatabaseName);
        Assert.Equal("Data", container.ContainerName);
        Assert.Equal("/pk", container.PartitionKeyPath);
    }

    [Fact]
    public void CoreDatabaseId_FollowsAnExplicitCoreConfiguration()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--operation", "publish"], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin("login", configuration =>
            configuration.Core = new CloudLoginCoreConfiguration { DatabaseId = "LoginStaging" });

        login.WithReference(builder.AddAzureCosmosDB("cosmos"));

        AzureCosmosDBDatabaseResource database = Assert.Single(builder.Resources.OfType<AzureCosmosDBDatabaseResource>());
        Assert.Equal("LoginStaging", database.DatabaseName);

        // The seven core containers plus the signing-key fallback, which is declared even when
        // unused because the server has no control-plane rights to create it later.
        Assert.Equal(8, builder.Resources.OfType<AzureCosmosDBContainerResource>().Count());
    }

    [Fact]
    public async Task DatabaseVersionV3_ProjectsNoLegacyCosmosKeys()
    {
        // V3 opens its own database with fixed container names; sending the legacy names would
        // describe storage the server never touches.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin();
        login.WithReference(builder.AddAzureCosmosDB("cosmos").RunAsEmulator());

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);

        Assert.Contains("Cosmos:ConnectionString", environment);
        Assert.DoesNotContain("Cosmos:DatabaseId", environment.Keys);
        Assert.DoesNotContain("Cosmos:ContainerId", environment.Keys);
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
        Assert.Contains("ConnectionStrings__cosmos", environment);

        // ...and only CloudLogin's own overload declares the storage schema.
        Assert.NotEmpty(builder.Resources.OfType<AzureCosmosDBDatabaseResource>());
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

    /// <summary>
    /// Booleans have to survive the projection. <c>System.Boolean</c> is the one primitive that
    /// does not implement <see cref="IFormattable"/>, so a formatter written around that interface
    /// drops every bool without a word — the AppHost sets the value, the service never receives it,
    /// and the feature simply does not turn on. Test mode was invisible in a deployed environment
    /// for exactly this reason.
    /// </summary>
    [Fact]
    public async Task Projection_CarriesBooleanConfiguration()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin("login", configuration =>
            configuration.AddTestMode());

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);

        Assert.True(
            environment.TryGetValue("TestMode:IsEnabled", out object? isEnabled),
            "TestMode:IsEnabled was dropped from the projection, so the service can never see it.");

        Assert.Equal("true", isEnabled?.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task Projection_CarriesFalseAsWellAsTrue()
    {
        // A disabled flag has to arrive too: the service seeds its own defaults, and "absent"
        // cannot be relied on to mean "off" for a property whose default is on.
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true });

        ICloudLoginServerBuilder login = builder.AddCloudLogin("login", configuration =>
            configuration.AddTestMode(isEnabled: false));

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);

        Assert.True(environment.TryGetValue("TestMode:IsEnabled", out object? isEnabled));
        Assert.Equal("false", isEnabled?.ToString(), ignoreCase: true);
    }

    // ── Identity HMAC wiring ──────────────────────────────────────────────────

    private const string SecretVariable = "CloudLogin__IdentityHmacSecret";
    private const string FallbackSecretsVariable = "CloudLogin__IdentityHmacFallbackSecrets";

    private static IDistributedApplicationBuilder NewBuilder() =>
        DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });

    private static ParameterResource IdentitySecretParameter(IDistributedApplicationBuilder builder, string resourceName = "login") =>
        builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == $"{resourceName}-identity-hmac");

    [Fact]
    public async Task AddCloudLogin_SuppliesTheIdentitySecretAutomatically()
    {
        // The server requires the secret and will not invent one, so an AppHost that had to be
        // told to add it would simply fail to start by default.
        IDistributedApplicationBuilder builder = NewBuilder();
        IResourceBuilder<ProjectResource> login = builder.AddCloudLogin();

        Assert.Contains(SecretVariable, await ReadEnvironmentAsync(login.Resource));
    }

    [Fact]
    public void TheGeneratedSecret_IsThirtyTwoRandomBytesBase64Encoded()
    {
        IDistributedApplicationBuilder builder = NewBuilder();
        builder.AddCloudLogin();

        string value = IdentitySecretParameter(builder).Value;

        Assert.Equal(32, Convert.FromBase64String(value).Length);

        // And it is what the server will actually accept - the generator and the validator agree.
        Assert.NotNull(AngryMonkey.CloudLogin.Server.Core.Domain.IdentityKeyHasher.FromConfiguredSecret(value));
    }

    [Fact]
    public void TheGeneratedSecret_IsDifferentEveryTimeOneIsMinted()
    {
        // Two AppHosts are two deployments with two identity indexes; sharing a key would let one
        // read the other's rows.
        IDistributedApplicationBuilder first = NewBuilder();
        first.AddCloudLogin();

        IDistributedApplicationBuilder second = NewBuilder();
        second.AddCloudLogin();

        Assert.NotEqual(IdentitySecretParameter(first).Value, IdentitySecretParameter(second).Value);
    }

    [Fact]
    public void TheGeneratedSecret_IsMarkedSecretAndPersisted()
    {
        // Secret so it is redacted in the dashboard and never published as a literal; persisted so
        // the same value comes back on the next run - a fresh key would orphan every account.
        IDistributedApplicationBuilder builder = NewBuilder();
        builder.AddCloudLogin();

        ParameterResource parameter = IdentitySecretParameter(builder);

        Assert.True(parameter.Secret);
        Assert.NotNull(parameter.Default);
    }

    [Fact]
    public void APersistedSecret_IsReusedInsteadOfRegenerated()
    {
        // What "persist" buys, from the server's point of view. Aspire writes the generated value
        // into the AppHost's user secrets on first run; on the next run it is present in
        // configuration, and the parameter must prefer it over minting a fresh one. A regenerated
        // key would resolve none of the rows the previous run wrote.
        string fromPreviousRun = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        IDistributedApplicationBuilder builder = NewBuilder();
        builder.Configuration["Parameters:login-identity-hmac"] = fromPreviousRun;

        builder.AddCloudLogin();

        Assert.Equal(fromPreviousRun, IdentitySecretParameter(builder).Value);
    }

    [Fact]
    public void TheSecret_IsStableWithinOneApplicationModel()
    {
        // Several reads - the dashboard, the environment callback, the manifest writer - must all
        // see one value. A parameter that regenerated per read would key writes and reads
        // differently inside a single run.
        IDistributedApplicationBuilder builder = NewBuilder();
        builder.AddCloudLogin();

        ParameterResource parameter = IdentitySecretParameter(builder);

        Assert.Equal(parameter.Value, parameter.Value);
    }

    [Fact]
    public void EachCloudLoginResource_GetsItsOwnSecret()
    {
        // Two authorities in one AppHost are two identity indexes.
        IDistributedApplicationBuilder builder = NewBuilder();
        builder.AddCloudLogin("login");
        builder.AddCloudLogin("partner-login");

        Assert.NotEqual(
            IdentitySecretParameter(builder, "login").Value,
            IdentitySecretParameter(builder, "partner-login").Value);
    }

    [Fact]
    public async Task WithIdentityHmacSecret_OverridesTheGeneratedOne()
    {
        string chosen = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        IDistributedApplicationBuilder builder = NewBuilder();
        builder.Configuration["Parameters:my-own-hmac"] = chosen;

        ICloudLoginServerBuilder login = builder.AddCloudLogin();
        login.WithIdentityHmacSecret(builder.AddParameter("my-own-hmac", secret: true));

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);

        // One variable, carrying the override rather than the generated parameter. The value is
        // the parameter resource itself - never a literal - so the assertion is on which one.
        ParameterResource injected = Assert.IsType<ParameterResource>(environment[SecretVariable]);

        Assert.Equal("my-own-hmac", injected.Name);
        Assert.Equal(chosen, injected.Value);

        // And the generated parameter it replaced is not also injected somewhere.
        Assert.Single(environment.Values.OfType<ParameterResource>(),
            parameter => parameter.Name.Contains("hmac", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WithIdentityHmacFallbackSecrets_UsesOneSecretJsonArraySetting()
    {
        string first = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        string second = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        string json = System.Text.Json.JsonSerializer.Serialize(new[] { first, second });
        IDistributedApplicationBuilder builder = NewBuilder();
        builder.Configuration["Parameters:identity-hmac-fallbacks"] = json;
        IResourceBuilder<ParameterResource> fallbackParameter =
            builder.AddParameter("identity-hmac-fallbacks", secret: true);
        ICloudLoginServerBuilder login = builder.AddCloudLogin();

        login.WithIdentityHmacFallbackSecrets(fallbackParameter);

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);
        ParameterResource injected =
            Assert.IsType<ParameterResource>(environment[FallbackSecretsVariable]);

        Assert.True(injected.Secret);
        Assert.Equal(json, injected.Value);
        Assert.DoesNotContain(environment.Keys, key =>
            key.StartsWith($"{FallbackSecretsVariable}__", StringComparison.Ordinal));
        Assert.DoesNotContain("CloudLogin:IdentityHmacFallbackSecrets", environment);
    }

    [Fact]
    public async Task TheSecret_IsInjectedUnderThePortableVariableName()
    {
        // Double underscores, not a colon: Linux App Service and containers reject a colon in an
        // environment variable name, so the colon form would silently never arrive.
        IDistributedApplicationBuilder builder = NewBuilder();
        IResourceBuilder<ProjectResource> login = builder.AddCloudLogin();

        Dictionary<string, object> environment = await ReadEnvironmentAsync(login.Resource);

        Assert.Contains("CloudLogin__IdentityHmacSecret", environment);
        Assert.DoesNotContain("CloudLogin:IdentityHmacSecret", environment);
    }

    [Fact]
    public async Task TheSecretValue_NeverAppearsInThePublishedManifest()
    {
        // The manifest is committed, shared and diffed. What belongs in it is a description of how
        // to generate the secret, which the deployment resolves once per environment - never the
        // secret itself.
        IDistributedApplicationBuilder builder = NewBuilder();
        IResourceBuilder<ProjectResource> login = builder.AddCloudLogin();

        string generated = IdentitySecretParameter(builder).Value;

        Dictionary<string, object> environment =
            await ReadEnvironmentAsync(login.Resource, DistributedApplicationOperation.Publish);

        string rendered = string.Join("\n", environment.Select(entry => $"{entry.Key}={entry.Value}"));
        Assert.DoesNotContain(generated, rendered, StringComparison.Ordinal);

        // What is emitted is a manifest expression pointing at the parameter, not its value.
        ParameterResource parameter = Assert.IsType<ParameterResource>(environment[SecretVariable]);

        Assert.Equal("login-identity-hmac", parameter.Name);
        Assert.DoesNotContain(generated, parameter.ValueExpression, StringComparison.Ordinal);
        Assert.Contains("login-identity-hmac", parameter.ValueExpression);
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
