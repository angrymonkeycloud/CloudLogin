using AngryMonkey.CloudLogin.Aspire;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AngryMonkey.CloudLogin.Tests;

public sealed class CloudLoginConfigurationPrecedenceTests
{
    [Fact]
    public void AppHostValues_OverrideProjectDefaultsWithoutDiscardingUnspecifiedDefaults()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CloudLogin:PrimaryColor"] = "#222222",
            ["Cosmos:AccountEndpoint"] = "https://host.documents.azure.com:443/",
            ["Cosmos:DatabaseId"] = "HostUsers",
            ["Storage:BlobEndpoint"] = "https://hoststorage.blob.core.windows.net/",
            ["Microsoft:ClientId"] = "host-client",
            ["Microsoft:Label"] = "Host Microsoft"
        });

        IConfiguration projectProviders = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Microsoft:ClientId"] = "project-client",
                ["Microsoft:ClientSecret"] = "project-secret",
                ["Microsoft:Label"] = "Project Microsoft",
                ["Microsoft:TenantId"] = "organizations",
                ["Microsoft:Audience"] = "MultipleTenant",
                ["Google:ClientId"] = "project-google",
                ["Google:ClientSecret"] = "project-google-secret",
                ["Google:Label"] = "Project Google"
            })
            .Build();
        StubCredential credential = new();

        CloudLoginWebConfiguration configuration = builder.ReadCloudLoginConfiguration(options =>
        {
            options.PrimaryColor = "#111111";
            options.Cosmos.AccountEndpoint = "https://project.documents.azure.com:443/";
            options.Cosmos.DatabaseId = "ProjectUsers";
            options.Cosmos.ContainerId = "ProjectData";
            options.Cosmos.IncludeLegacySchema = true;
            options.AzureStorage = new AzureStorageConfiguration
            {
                AccountName = "projectstorage",
                ContainerName = "projectusers"
            };
            options.AllowedMobileSchemes.Add("blusky");
            options.Providers =
            [
                new LoginProviders.MicrosoftProviderConfiguration(projectProviders.GetSection("Microsoft")),
                new LoginProviders.GoogleProviderConfiguration(projectProviders.GetSection("Google"))
            ];
        });

        configuration.BindAspireResources(builder, credential);

        Assert.Equal("#222222", configuration.PrimaryColor);
        Assert.Equal("https://host.documents.azure.com:443/", configuration.Cosmos.AccountEndpoint);
        Assert.Equal("HostUsers", configuration.Cosmos.DatabaseId);
        Assert.Equal("ProjectData", configuration.Cosmos.ContainerId);
        Assert.True(configuration.Cosmos.IncludeLegacySchema);
        Assert.Same(credential, configuration.Cosmos.Credential);
        Assert.Equal("https://hoststorage.blob.core.windows.net/", configuration.AzureStorage?.BlobEndpoint?.AbsoluteUri);
        Assert.Equal("projectusers", configuration.AzureStorage?.ContainerName);
        Assert.Same(credential, configuration.AzureStorage?.Credential);
        Assert.Equal(["blusky"], configuration.AllowedMobileSchemes);

        LoginProviders.MicrosoftProviderConfiguration microsoft = Assert.IsType<LoginProviders.MicrosoftProviderConfiguration>(
            Assert.Single(configuration.Providers, provider => provider.Code.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)));
        LoginProviders.GoogleProviderConfiguration google = Assert.IsType<LoginProviders.GoogleProviderConfiguration>(
            Assert.Single(configuration.Providers, provider => provider.Code.Equals("Google", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal("host-client", microsoft.ClientId);
        Assert.Equal("project-secret", microsoft.ClientSecret);
        Assert.Equal("Host Microsoft", microsoft.Label);
        Assert.Equal("organizations", microsoft.TenantId);
        Assert.Equal(MicrosoftProviderAudience.MultipleTenant, microsoft.Audience);
        Assert.Equal("project-google", google.ClientId);
    }

    private sealed class StubCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
