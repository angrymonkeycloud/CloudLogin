// CloudLoginServer/Services/ProviderConfigurationService.cs
using AngryMonkey.CloudLogin.Sever.Providers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
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
            });
        }
        else
        {
            builder.AddOpenIdConnect("Microsoft", async options =>
            {
                options.SignInScheme = "Cookies";
                string audiencePath = provider.Audience switch { MicrosoftProviderAudience.Personal => "consumers", _ => provider.TenantId! };
                ConfigureMicrosoftOpenIdConnect(options, provider, audiencePath);
            });
        }
    }

    private async void ConfigureMicrosoftOpenIdConnect(OpenIdConnectOptions options, LoginProviders.MicrosoftProviderConfiguration provider, string audiencePath)
    {
        options.ClientId = provider.ClientId;
        options.Authority = $"https://login.microsoftonline.com/{audiencePath}/v2.0/";
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Clear();
        options.Scope.Add("openid"); options.Scope.Add("profile"); options.Scope.Add("email"); options.Scope.Add("User.Read");
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
        options.ClaimActions.MapJsonKey("locale", "locale");
    }

    private async Task ConfigureMicrosoftOpenIdEvents(OpenIdConnectOptions options, LoginProviders.MicrosoftProviderConfiguration provider)
    {
        X509Certificate2 certificate = await provider.GetCertificate();
        options.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = options.Authority, ValidAudiences = [options.ClientId], NameClaimType = ClaimTypes.Name };
        options.Events.OnAuthorizationCodeReceived = async context => { await HandleMicrosoftAuthorizationCode(context, certificate, options); };
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
        });
    }

    private void ConfigureTwitterProvider(AuthenticationBuilder builder, LoginProviders.TwitterProviderConfiguration provider)
    {
        builder.AddTwitter(options =>
        {
            options.SignInScheme = "Cookies";
            options.ConsumerKey = provider.ClientId;
            options.ConsumerSecret = provider.ClientSecret;
        });
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
        AuthenticationResult result = await confidentialClient.AcquireTokenByAuthorizationCode(["User.Read"], context.ProtocolMessage.Code).WithPkceCodeVerifier(codeVerifier).ExecuteAsync();
        await ProcessMicrosoftTokens(context, result);
    }

    private static async Task ProcessMicrosoftTokens(AuthorizationCodeReceivedContext context, AuthenticationResult result)
    {
        JwtSecurityTokenHandler handler = new();
        JwtSecurityToken? accessToken = handler.ReadToken(result.AccessToken) as JwtSecurityToken;
        JwtSecurityToken? idToken = handler.ReadToken(result.IdToken) as JwtSecurityToken;
        ClaimsIdentity claimsIdentity = new("Microsoft");
        AddMicrosoftClaims(claimsIdentity, accessToken, idToken);
        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);
        await context.HttpContext.SignInAsync("Cookies", claimsPrincipal);
        context.HandleCodeRedemption(result.AccessToken, result.IdToken);
    }

    private static void AddMicrosoftClaims(ClaimsIdentity identity, JwtSecurityToken? accessToken, JwtSecurityToken? idToken)
    {
        if (accessToken == null) return;
        string? givenName = accessToken.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
        string? surname = accessToken.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value;
        string? email = accessToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? accessToken.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value;
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, accessToken.Claims.FirstOrDefault(c => c.Type == "oid")?.Value ?? string.Empty));
        if (!string.IsNullOrEmpty(email)) identity.AddClaim(new Claim(ClaimTypes.Email, email));
        if (!string.IsNullOrEmpty(givenName)) identity.AddClaim(new Claim(ClaimTypes.GivenName, givenName));
        if (!string.IsNullOrEmpty(surname)) identity.AddClaim(new Claim(ClaimTypes.Surname, surname));
        AddAdditionalClaims(identity, accessToken, idToken);
    }

    private static void AddAdditionalClaims(ClaimsIdentity identity, JwtSecurityToken accessToken, JwtSecurityToken? idToken)
    {
        IEnumerable<Claim> allClaims = accessToken.Claims.Concat(idToken?.Claims ?? Enumerable.Empty<Claim>());

        foreach (Claim? claim in allClaims)
            if (!identity.HasClaim(claim.Type, claim.Value))
                identity.AddClaim(claim);
    }
}
