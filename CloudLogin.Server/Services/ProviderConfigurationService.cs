// CloudLoginServer/Services/ProviderConfigurationService.cs
using AngryMonkey.CloudLogin.Sever.Providers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Specialized;

using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Validators;
using System.Web;

namespace AngryMonkey.CloudLogin.Server;

public class ProviderConfigurationService
{
    private readonly CloudLoginWebConfiguration _configuration;

    public ProviderConfigurationService(CloudLoginWebConfiguration configuration)
    {
        _configuration = configuration;
    }

    public AuthenticationBuilder ConfigureProviders(AuthenticationBuilder builder)
    {
        foreach (ProviderConfiguration provider in _configuration.Providers)
        {
            ConfigureProvider(builder, provider);
        }

        return builder;
    }

    private void ConfigureProvider(AuthenticationBuilder builder, ProviderConfiguration provider)
    {
        switch (provider)
        {
            case LoginProviders.MicrosoftProviderConfiguration microsoftProvider:
                ConfigureMicrosoftProvider(builder, microsoftProvider);
                break;

            case LoginProviders.GoogleProviderConfiguration googleProvider:
                ConfigureGoogleProvider(builder, googleProvider);
                break;

            case LoginProviders.FacebookProviderConfiguration facebookProvider:
                ConfigureFacebookProvider(builder, facebookProvider);
                break;

            case LoginProviders.TwitterProviderConfiguration twitterProvider:
                ConfigureTwitterProvider(builder, twitterProvider);
                break;

            case LoginProviders.WhatsAppProviderConfiguration whatsAppProvider:
                ConfigureWhatsAppProvider(builder, whatsAppProvider);
                break;
        }
    }

    private void ConfigureMicrosoftProvider(AuthenticationBuilder builder, LoginProviders.MicrosoftProviderConfiguration provider)
    {
        if (!string.IsNullOrEmpty(provider.ClientSecret))
        {
            builder.AddMicrosoftAccount(options =>
            {
                options.SignInScheme = "Cookies";
                options.ClientId = provider.ClientId;
                options.ClientSecret = provider.ClientSecret;
                options.SaveTokens = true;
                options.Events.OnRemoteFailure = HandleRemoteFailure;
            });
        }
        else
        {
            builder.AddOpenIdConnect("Microsoft", options =>
            {
                options.SignInScheme = "Cookies";
                string audiencePath = provider.Audience switch
                {
                    MicrosoftProviderAudience.Personal => "consumers",
                    MicrosoftProviderAudience.MultipleTenant => "organizations",
                    MicrosoftProviderAudience.All => "common",
                    MicrosoftProviderAudience.SingleTenant when !string.IsNullOrWhiteSpace(provider.TenantId) => provider.TenantId,
                    _ => throw new InvalidOperationException("Microsoft:TenantId is required for single-tenant sign-in.")
                };
                ConfigureMicrosoftOpenIdConnect(options, provider, audiencePath);
            });
        }
    }

    private void ConfigureMicrosoftOpenIdConnect(OpenIdConnectOptions options, LoginProviders.MicrosoftProviderConfiguration provider, string audiencePath)
    {
        options.ClientId = provider.ClientId;
        options.Authority = $"https://login.microsoftonline.com/{audiencePath}/v2.0/";
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.CallbackPath = "/signin-microsoft";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Clear();
        ConfigureMicrosoftOpenIdScopes(options);
        ConfigureMicrosoftOpenIdClaims(options);
        ConfigureMicrosoftOpenIdIssuerValidation(options, provider.Audience);
        ConfigureMicrosoftOpenIdEvents(options, provider);
    }

    private static void ConfigureMicrosoftOpenIdIssuerValidation(OpenIdConnectOptions options, MicrosoftProviderAudience audience)
    {
        options.TokenValidationParameters.IssuerValidator = AadIssuerValidator.GetAadIssuerValidator(options.Authority).Validate;
        options.TokenValidationParameters.EnableAadSigningKeyIssuerValidation();
    }

    /// <summary>Validates a Microsoft issuer against the token's tenant and trusted issuer metadata.</summary>
    public static string ValidateMicrosoftIssuer(string? issuer, SecurityToken token, TokenValidationParameters parameters) =>
        string.IsNullOrWhiteSpace(issuer)
            ? throw new SecurityTokenInvalidIssuerException("A Microsoft token must contain an issuer.")
            : AadIssuerValidator.GetAadIssuerValidator("https://login.microsoftonline.com/common/v2.0").Validate(issuer, token, parameters);

    private void ConfigureMicrosoftOpenIdScopes(OpenIdConnectOptions options)
    {
        options.Scope.Add(OpenIdConnectScope.OpenId);
        options.Scope.Add(OpenIdConnectScope.Profile);
        options.Scope.Add("User.Read");
        options.Scope.Add(OpenIdConnectScope.Email);
        options.Scope.Add(OpenIdConnectScope.Phone);
        options.Scope.Add(OpenIdConnectScope.OfflineAccess);
        options.Scope.Add(OpenIdConnectScope.Address);
    }

    private void ConfigureMicrosoftOpenIdClaims(OpenIdConnectOptions options)
    {
        options.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "given_name");
        options.ClaimActions.MapJsonKey(ClaimTypes.Surname, "family_name");
        options.ClaimActions.MapJsonKey("locale", "locale");
    }

    private void ConfigureMicrosoftOpenIdEvents(
        OpenIdConnectOptions options,
        LoginProviders.MicrosoftProviderConfiguration provider)
    {
        options.TokenValidationParameters.NameClaimType = ClaimTypes.Name;
        options.Events.OnAuthorizationCodeReceived = async context =>
        {
            using X509Certificate2 certificate = await provider.GetCertificate(context.HttpContext.RequestAborted);
            await HandleMicrosoftAuthorizationCode(context, certificate, options);
        };
        options.Events.OnRemoteFailure = HandleRemoteFailure;
    }

    private void ConfigureGoogleProvider(AuthenticationBuilder builder, LoginProviders.GoogleProviderConfiguration provider)
    {
        builder.AddGoogle(options =>
        {
            options.SignInScheme = "Cookies";
            options.ClientId = provider.ClientId;
            options.ClientSecret = provider.ClientSecret;
            options.SaveTokens = true;
            options.ClaimActions.MapJsonKey("picture", "picture");
            options.ClaimActions.MapJsonKey("locale", "locale");
            options.Events.OnRemoteFailure = HandleRemoteFailure;
        });
    }

    private void ConfigureFacebookProvider(AuthenticationBuilder builder, LoginProviders.FacebookProviderConfiguration provider)
    {
        builder.AddFacebook(options =>
        {
            options.SignInScheme = "Cookies";
            options.ClientId = provider.ClientId;
            options.ClientSecret = provider.ClientSecret;
            options.SaveTokens = true;
            options.Fields.Add("email"); options.Fields.Add("name"); options.Fields.Add("first_name"); options.Fields.Add("last_name"); options.Fields.Add("picture"); options.Fields.Add("birthday"); options.Fields.Add("locale");
            options.ClaimActions.MapCustomJson("picture", user =>
     {
         try
         {
             if (user.TryGetProperty("picture", out System.Text.Json.JsonElement picture) && picture.TryGetProperty("data", out System.Text.Json.JsonElement data) && data.TryGetProperty("url", out System.Text.Json.JsonElement url) && url.GetString() is string s)
                 return s;
         }
         catch { }
         return null;
     });
            options.ClaimActions.MapJsonKey(ClaimTypes.DateOfBirth, "birthday");
            options.ClaimActions.MapJsonKey("locale", "locale");
            options.Events.OnRemoteFailure = HandleRemoteFailure;
        });
    }

    private void ConfigureTwitterProvider(AuthenticationBuilder builder, LoginProviders.TwitterProviderConfiguration provider)
    {
        builder.AddTwitter(options =>
        {
            options.SignInScheme = "Cookies";
            options.ConsumerKey = provider.ClientId;
            options.ConsumerSecret = provider.ClientSecret;
            options.Events.OnRemoteFailure = HandleRemoteFailure;
        });
    }

    /// <summary>
    /// Runs when a user cancels or is denied at the external provider (e.g. clicking
    /// "Cancel" on Google's consent screen). Without this, ASP.NET Core's remote
    /// authentication handler rethrows the provider's error as an unhandled
    /// <see cref="AuthenticationFailureException"/>, crashing the request instead of
    /// letting the user pick another provider. Send them back to the login picker,
    /// preserving the original referer so the surrounding flow isn't lost.
    /// </summary>
    private static Task HandleRemoteFailure(RemoteFailureContext context)
    {
        context.HandleResponse();

        NameValueCollection query = HttpUtility.ParseQueryString(string.Empty);

        if (context.Properties?.Items.TryGetValue("referer", out string? referer) == true && !string.IsNullOrEmpty(referer))
            query["referer"] = referer;

        if (context.Properties?.Items.TryGetValue("isMobileApp", out string? isMobileApp) == true && isMobileApp == "true")
            query["isMobileApp"] = "true";

        string baseUrl = $"{context.HttpContext.Request.Scheme}://{context.HttpContext.Request.Host}";
        string queryString = query.Count > 0 ? $"?{query}" : string.Empty;

        context.HttpContext.Response.Redirect($"{baseUrl}/{queryString}");

        return Task.CompletedTask;
    }

    private void ConfigureWhatsAppProvider(AuthenticationBuilder builder, LoginProviders.WhatsAppProviderConfiguration provider) { }

    private static async Task HandleMicrosoftAuthorizationCode(AuthorizationCodeReceivedContext context, X509Certificate2 certificate, OpenIdConnectOptions options)
    {
        string codeVerifier = context.TokenEndpointRequest.Parameters["code_verifier"];
        string url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";
        IConfidentialClientApplication confidentialClient = ConfidentialClientApplicationBuilder.Create(options.ClientId)
        .WithRedirectUri(url)
        .WithCertificate(certificate)
        .WithAuthority(new Uri(options.Authority))
        .Build();
        AuthenticationResult result = await confidentialClient.AcquireTokenByAuthorizationCode(["User.Read"], context.ProtocolMessage.Code).WithPkceCodeVerifier(codeVerifier).ExecuteAsync(context.HttpContext.RequestAborted);
        context.HandleCodeRedemption(result.AccessToken, result.IdToken);
    }

}
