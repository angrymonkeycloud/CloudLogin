using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginConfigurationSecurityTests
{
    [Fact]
    public void Defaults_AreValidForProduction()
    {
        CloudLoginWebConfiguration configuration = new();

        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false);

        Assert.StartsWith("__Host-", configuration.CookieName);
        Assert.True(configuration.Security.RequireHttps);
        Assert.Equal(600_000, configuration.Security.PasswordHashIterations);
        Assert.Equal("#0078D4", configuration.PrimaryColor);
    }
    [Theory]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("#12")]
    [InlineData("#12345G")]
    public void PrimaryColor_RejectsInvalidValues(string color)
    {
        CloudLoginWebConfiguration configuration = new() { PrimaryColor = color };

        Assert.Throws<InvalidOperationException>(() =>
            CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));
    }

    [Fact]
    public void Production_RejectsDisabledHttps()
    {
        CloudLoginWebConfiguration configuration = new();
        configuration.Security.RequireHttps = false;

        Assert.Throws<InvalidOperationException>(() =>
            CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));
    }

    [Theory]
    [InlineData("http://app.example")]
    [InlineData("https://app.example/path")]
    [InlineData("https://user:password@app.example")]
    public void RedirectAllowlist_RejectsUnsafeOrNonOriginValues(string origin)
    {
        CloudLoginWebConfiguration configuration = new();
        configuration.AllowedRedirectOrigins.Add(origin);

        Assert.Throws<InvalidOperationException>(() =>
            CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));
    }

    [Fact]
    public void HostCookie_RejectsSharedCookieDomain()
    {
        CloudLoginWebConfiguration configuration = new() { CookieDomain = ".example.com" };

        Assert.Throws<InvalidOperationException>(() =>
            CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));
    }

    [Fact]
    public void Production_KeepsExplicitlyEnabledTestMode()
    {
        IConfiguration configurationValues = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestMode:IsEnabled"] = "true"
            })
            .Build();
        CloudLoginWebConfiguration configuration = new();
        configuration.Providers.Add(new LoginTestProviders.TestModeConfiguration(
            configurationValues.GetSection("TestMode")));

        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false);

        Assert.Contains(configuration.Providers,
            provider => provider is LoginTestProviders.TestModeConfiguration);
    }

    [Fact]
    public void Development_KeepsExplicitlyEnabledTestMode()
    {
        IConfiguration configurationValues = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestMode:IsEnabled"] = "true"
            })
            .Build();
        CloudLoginWebConfiguration configuration = new();
        configuration.Providers.Add(new LoginTestProviders.TestModeConfiguration(
            configurationValues.GetSection("TestMode")));

        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true);

        Assert.Contains(configuration.Providers,
            provider => provider is LoginTestProviders.TestModeConfiguration);
    }

    [Fact]
    public void RedirectAndMobileAllowlists_AreOptional()
    {
        CloudLoginWebConfiguration configuration = new();

        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false);

        Assert.Empty(configuration.AllowedRedirectOrigins);
        Assert.Empty(configuration.AllowedMobileSchemes);
    }

    [Fact]
    public void Development_OmitsMicrosoftUntilCredentialsAreAvailable()
    {
        CloudLoginWebConfiguration configuration = new();
        configuration.Providers.Add(new LoginProviders.MicrosoftProviderConfiguration());

        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: true);

        Assert.DoesNotContain(configuration.Providers,
            provider => provider is LoginProviders.MicrosoftProviderConfiguration);
    }

    [Fact]
    public void Production_RejectsMicrosoftWithoutCredentials()
    {
        CloudLoginWebConfiguration configuration = new();
        configuration.Providers.Add(new LoginProviders.MicrosoftProviderConfiguration());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));

        Assert.Contains("ClientId", exception.Message, StringComparison.Ordinal);
    }

    // The verification-code provider used to be refused outright, because the code was created and
    // checked in browser code. It is now issued, counted and checked on the server, so it is an
    // ordinary provider that any environment may configure.
    [Fact]
    public void VerificationCodeProvider_IsAcceptedInEveryEnvironment()
    {
        IConfiguration configurationValues = new ConfigurationBuilder().Build();
        CloudLoginWebConfiguration configuration = new();
        configuration.Providers.Add(new LoginProviders.CodeProviderConfiguration(
            configurationValues.GetSection("Code")));

        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false);
    }

    // What stays refused is the deprecated endpoint set that lets browser code choose the code.
    [Fact]
    public void LegacyClientManagedVerificationCodes_CannotBeEnabledOutsideDevelopment()
    {
        CloudLoginWebConfiguration configuration = new();
        configuration.Security.EnableLegacyClientVerificationCodes = true;

        Assert.Throws<InvalidOperationException>(() =>
            CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));
    }

    [Theory]
    [InlineData(3, 5, 5)]
    [InlineData(6, 0, 5)]
    [InlineData(6, 5, 0)]
    public void VerificationCodeSettings_AreRejectedWhenTheyWouldWeakenTheCode(
        int length, int attempts, int lifetimeMinutes)
    {
        CloudLoginWebConfiguration configuration = new();
        configuration.Security.VerificationCodeLength = length;
        configuration.Security.MaximumVerificationAttempts = attempts;
        configuration.Security.VerificationCodeLifetime = TimeSpan.FromMinutes(lifetimeMinutes);

        Assert.Throws<InvalidOperationException>(() =>
            CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));
    }
}
