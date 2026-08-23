using AngryMonkey.CloudLogin.Server.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

public static class CloudLoginTokenIssuerServiceExtensions
{
    /// <summary>
    /// Turns this CloudLogin instance into a token authority: it gains signing keys,
    /// a JWKS endpoint, and the token routes under <c>/CloudLogin/Token</c>.
    /// <para>
    /// Call this on the login application only. Relying parties call
    /// <c>AddCloudLoginTokenAuthentication</c> instead &mdash; they verify tokens, and
    /// deliberately have no ability to mint them.
    /// </para>
    /// </summary>
    public static IServiceCollection AddCloudLoginTokenIssuer(
        this IServiceCollection services,
        Action<CloudLoginTokenOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddDataProtection();

        services.AddOptions<CloudLoginTokenOptions>()
            .Configure(configure)
            .Validate(
                options =>
                {
                    options.Validate();
                    return true;
                },
                "CloudLogin token issuer configuration is invalid.")
            .ValidateOnStart();

        services.TryAddSingletonTokenStore();
        services.AddSingleton<CloudLoginSigningKeyManager>();
        services.AddScoped<CloudLoginTokenService>();
        services.AddHostedService<CloudLoginSigningKeyBootstrap>();

        return services;
    }

    /// <summary>
    /// Configures the token issuer from a configuration section, so audiences and
    /// service-client secrets come from configuration or a secret store rather than
    /// being written into source.
    /// </summary>
    /// <example>
    /// <code>
    /// "CloudLoginTokens": {
    ///   "Issuer": "https://login.example.com",
    ///   "AllowedAudiences": [ "portal", "cdm-api" ],
    ///   "ServiceClients": {
    ///     "portal": {
    ///       "ClientId": "portal",
    ///       "SecretHash": "&lt;base64 SHA-256 of the secret&gt;",
    ///       "AllowedAudiences": [ "cdm-api" ]
    ///     }
    ///   }
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddCloudLoginTokenIssuer(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CloudLoginTokenOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddCloudLoginTokenIssuer(options =>
        {
            configuration.Bind(options);
            configure?.Invoke(options);
        });
    }

    private static void TryAddSingletonTokenStore(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ICloudLoginTokenStore)))
            return;

        services.AddSingleton<ICloudLoginTokenStore, CosmosTokenStore>();
    }
}

/// <summary>
/// Ensures a signing key exists before the first token request arrives, so a cold
/// start never pays for key generation on a user-facing request, and a misconfigured
/// key store fails loudly at boot rather than silently at first sign-in.
/// </summary>
internal sealed class CloudLoginSigningKeyBootstrap(
    CloudLoginSigningKeyManager keyManager,
    ILogger<CloudLoginSigningKeyBootstrap> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await keyManager.GetSigningCredentialsAsync(stoppingToken);
            logger.LogInformation("CloudLogin token signing key is ready.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to prepare the CloudLogin token signing key.");
        }
    }
}
