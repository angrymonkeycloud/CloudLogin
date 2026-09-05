using AngryMonkey.CloudLogin.Server;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// The Microsoft sign-in issuer validator installed for multi-tenant/personal audiences.
/// <para>
/// login.microsoftonline.com's shared /common, /organizations, and /consumers endpoints all
/// serve the same OpenID discovery document, whose "issuer" field is the literal, unresolved
/// "{tenantid}" template rather than a real value. The framework default validator compares
/// that template against the signed-in user's concrete issuer and rejects every sign-in - this
/// pins the replacement validator's behavior so that regression doesn't return silently (it
/// surfaces as an unauthenticated redirect with no visible error, not a build failure).
/// </para>
/// </summary>
public class MicrosoftIssuerValidationTests
{
    private static readonly SecurityToken Token = new JsonWebToken("{}", "{}");
    private static readonly TokenValidationParameters Parameters = new()
    {
        ValidIssuers = ["https://login.microsoftonline.com/{tenantid}/v2.0", "https://login.microsoftonline.com/{tenantid}/v2.0/", "https://login.windows.net/{tenantid}/v2.0"]
    };

    [Theory]
    [InlineData("https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47/v2.0")]
    [InlineData("https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0/")] // consumers (personal accounts) tenant
    [InlineData("https://login.windows.net/72f988bf-86f1-41af-91ab-2d7cd011db47/v2.0")]
    public void ValidateMicrosoftIssuer_AcceptsConcreteTenantIssuer(string issuer)
    {
        string tenant = new Uri(issuer).Segments[1].TrimEnd('/');
        SecurityToken token = new JsonWebToken("{}", System.Text.Json.JsonSerializer.Serialize(new { tid = tenant }));
        string result = ProviderConfigurationService.ValidateMicrosoftIssuer(issuer, token, Parameters);

        Assert.Equal(issuer, result);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/{tenantid}/v2.0")] // the unresolved discovery-document template
    [InlineData("https://login.microsoftonline.com/common/v2.0")]
    [InlineData("https://not-microsoft.example/72f988bf-86f1-41af-91ab-2d7cd011db47/v2.0")]
    [InlineData(null)]
    public void ValidateMicrosoftIssuer_RejectsAnythingElse(string? issuer)
    {
        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            ProviderConfigurationService.ValidateMicrosoftIssuer(issuer, Token, Parameters));
    }
    [Fact]
    public void MicrosoftIssuer_RejectsTokensFromADifferentTenant()
    {
        SecurityToken token = new JsonWebToken("{}", "{\"tid\":\"9188040d-6c67-4c5b-b112-36a304b66dad\"}");
        Assert.Throws<SecurityTokenInvalidIssuerException>(() => ProviderConfigurationService.ValidateMicrosoftIssuer(
            "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47/v2.0", token, Parameters));
    }
}
