using AngryMonkey.CloudLogin.Aspire.Hosting;
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

        CloudLoginProjectBuilder login = builder.AddCloudLogin();
        IResourceBuilder<ExecutableResource> consumer = builder
            .AddExecutable("api", "dotnet", ".")
            .WithHttpEndpoint()
            .WithReference(login);

        Assert.True(File.Exists(login.Resource.GetProjectMetadata().ProjectPath));
        Assert.Single(builder.Resources.OfType<AzureCosmosDBResource>());
        Assert.Single(builder.Resources.OfType<AzureStorageResource>());
        AzureCosmosDBDatabaseResource database = Assert.Single(builder.Resources.OfType<AzureCosmosDBDatabaseResource>());
        AzureCosmosDBContainerResource container = Assert.Single(builder.Resources.OfType<AzureCosmosDBContainerResource>());
        Assert.Equal("Users", database.DatabaseName);
        Assert.Equal("Data", container.ContainerName);
        Assert.Equal("/pk", container.PartitionKeyPath);

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

        builder.AddCloudLogin()
            .WithCloudLoginDatabase("Accounts", "Users");

        AzureCosmosDBDatabaseResource database = Assert.Single(builder.Resources.OfType<AzureCosmosDBDatabaseResource>());
        AzureCosmosDBContainerResource container = Assert.Single(builder.Resources.OfType<AzureCosmosDBContainerResource>());

        Assert.Equal("Accounts", database.DatabaseName);
        Assert.Equal("Users", container.ContainerName);
        Assert.Equal("/pk", container.PartitionKeyPath);
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

        CloudLoginProjectBuilder login = builder.AddCloudLogin();
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
