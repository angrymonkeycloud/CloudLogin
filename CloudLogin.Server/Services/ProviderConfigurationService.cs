// CloudLoginServer/Services/ProviderConfigurationService.cs
using AngryMonkey.CloudLogin.Sever.Providers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;

namespace AngryMonkey.CloudLogin.Server;

public class ProviderConfigurationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CloudLoginConfiguration _configuration;

    public ProviderConfigurationService(IServiceProvider serviceProvider, CloudLoginConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
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
            case MicrosoftProviderConfiguration microsoftProvider:
                ConfigureMicrosoftProvider(builder, microsoftProvider);
                break;

            case GoogleProviderConfiguration googleProvider:
                ConfigureGoogleProvider(builder, googleProvider);
                break;

            case FacebookProviderConfiguration facebookProvider:
                ConfigureFacebookProvider(builder, facebookProvider);
                break;

            case TwitterProviderConfiguration twitterProvider:
                ConfigureTwitterProvider(builder, twitterProvider);
                break;

            case WhatsAppProviderConfiguration whatsAppProvider:
                ConfigureWhatsAppProvider(builder, whatsAppProvider);
                break;
        }
    }

    private void ConfigureMicrosoftProvider(AuthenticationBuilder builder, MicrosoftProviderConfiguration provider)
    {
        if (!string.IsNullOrEmpty(provider.ClientSecret))
        {
            builder.AddMicrosoftAccount(options =>
            {
                options.SignInScheme = "Cookies";
                options.ClientId = provider.ClientId;
                options.ClientSecret = provider.ClientSecret;

                if (provider.Audience == MicrosoftProviderAudience.Personal)
                {
                    options.AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
                    options.TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
                    options.UserInformationEndpoint = "https://graph.microsoft.com/oidc/userinfo";
                }
            });
        }
        else
        {
            builder.AddOpenIdConnect("Microsoft", async options =>
            {
                options.SignInScheme = "Cookies";

                string audiencePath = provider.Audience switch
                {
                    MicrosoftProviderAudience.Personal => "consumers",
                    _ => provider.TenantId!
                };

                ConfigureMicrosoftOpenIdConnect(options, provider, audiencePath);
            });
        }
    }

    private async void ConfigureMicrosoftOpenIdConnect(OpenIdConnectOptions options, MicrosoftProviderConfiguration provider, string audiencePath)
    {
        options.ClientId = provider.ClientId;
        options.Authority = $"https://login.microsoftonline.com/{audiencePath}/v2.0/";
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        // Add required scopes
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("User.Read");

        ConfigureMicrosoftOpenIdScopes(options);
        ConfigureMicrosoftOpenIdClaims(options);
        await ConfigureMicrosoftOpenIdEvents(options, provider);
    }

    private void ConfigureMicrosoftOpenIdScopes(OpenIdConnectOptions options)
    {
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
    }

    private async Task ConfigureMicrosoftOpenIdEvents(OpenIdConnectOptions options, MicrosoftProviderConfiguration provider)
    {
        X509Certificate2 certificate = await provider.GetCertificate();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Authority,
            ValidAudiences = [options.ClientId],
            NameClaimType = ClaimTypes.Name
        };

        options.Events.OnAuthorizationCodeReceived = async context =>
        {
            await HandleMicrosoftAuthorizationCode(context, certificate, options);
        };
    }

    private void ConfigureGoogleProvider(AuthenticationBuilder builder, GoogleProviderConfiguration provider)
    {
        builder.AddGoogle(options =>
        {
            options.SignInScheme = "Cookies";
            options.ClientId = provider.ClientId;
            options.ClientSecret = provider.ClientSecret;
        });
    }

    private void ConfigureFacebookProvider(AuthenticationBuilder builder, FacebookProviderConfiguration provider)
    {
        builder.AddFacebook(options =>
        {
            options.SignInScheme = "Cookies";
            options.ClientId = provider.ClientId;
            options.ClientSecret = provider.ClientSecret;
        });
    }

    private void ConfigureTwitterProvider(AuthenticationBuilder builder, TwitterProviderConfiguration provider)
    {
        builder.AddTwitter(options =>
        {
            options.SignInScheme = "Cookies";
            options.ConsumerKey = provider.ClientId;
            options.ConsumerSecret = provider.ClientSecret;
        });
    }

    private void ConfigureWhatsAppProvider(AuthenticationBuilder builder, WhatsAppProviderConfiguration provider)
    {
        // WhatsApp specific configuration if needed
    }

    private static async Task HandleMicrosoftAuthorizationCode(AuthorizationCodeReceivedContext context, X509Certificate2 certificate, OpenIdConnectOptions options)
    {
        string codeVerifier = context.TokenEndpointRequest.Parameters["code_verifier"];
        string url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";

        IConfidentialClientApplication confidentialClient = ConfidentialClientApplicationBuilder.Create(options.ClientId)
            .WithRedirectUri(url)
            .WithCertificate(certificate)
            .WithAuthority(new Uri(options.Authority))
            .Build();

        AuthenticationResult result = await confidentialClient.AcquireTokenByAuthorizationCode(["User.Read"], context.ProtocolMessage.Code)
            .WithPkceCodeVerifier(codeVerifier)
            .ExecuteAsync();

        await ProcessMicrosoftTokens(context, result);
    }

    private static async Task ProcessMicrosoftTokens(AuthorizationCodeReceivedContext context, AuthenticationResult result)
    {
        JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
        JwtSecurityToken? accessToken = handler.ReadToken(result.AccessToken) as JwtSecurityToken;
        JwtSecurityToken? idToken = handler.ReadToken(result.IdToken) as JwtSecurityToken;

        ClaimsIdentity claimsIdentity = new ClaimsIdentity("Microsoft");
        AddMicrosoftClaims(claimsIdentity, accessToken, idToken);

        ClaimsPrincipal claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
        await context.HttpContext.SignInAsync("Cookies", claimsPrincipal);

        context.HandleCodeRedemption(result.AccessToken, result.IdToken);
    }

    private static void AddMicrosoftClaims(ClaimsIdentity identity, JwtSecurityToken? accessToken, JwtSecurityToken? idToken)
    {
        if (accessToken == null) return;

        string? givenName = accessToken.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
        string? surname = accessToken.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value;
        string? email = accessToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                   ?? accessToken.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value;

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier,
            accessToken.Claims.FirstOrDefault(c => c.Type == "oid")?.Value ?? string.Empty));

        if (!string.IsNullOrEmpty(email))
            identity.AddClaim(new Claim(ClaimTypes.Email, email));

        if (!string.IsNullOrEmpty(givenName))
            identity.AddClaim(new Claim(ClaimTypes.GivenName, givenName));

        if (!string.IsNullOrEmpty(surname))
            identity.AddClaim(new Claim(ClaimTypes.Surname, surname));

        AddAdditionalClaims(identity, accessToken, idToken);
    }

    private static void AddAdditionalClaims(ClaimsIdentity identity, JwtSecurityToken accessToken, JwtSecurityToken? idToken)
    {
        IEnumerable<Claim> allClaims = accessToken.Claims.Concat(idToken?.Claims ?? Enumerable.Empty<Claim>());
        foreach (Claim? claim in allClaims)
        {
            if (!identity.HasClaim(claim.Type, claim.Value))
                identity.AddClaim(claim);
        }
    }
}
