using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

internal sealed record TokenSet(string Access, string? Refresh, string SessionId, string Code, string Verifier, string Redirect);

internal static class TokenSets
{
    /// <summary>Runs a complete confidential sign-in for a person and redeems the code, the way an application's back end does.</summary>
    public static async Task<TokenSet> IssueAsync(
        AuthEcosystem ecosystem,
        ConsumerHost app,
        string secret,
        string email = AuthEcosystem.AuthorityEmail,
        TestBrowser? browser = null)
    {
        AuthorityClient client = new(ecosystem.Network);
        string verifier = CloudLoginPkce.CreateVerifier();
        string redirect = $"{app.Origin}/auth/callback";

        ApiResult begun = await client.BeginAsync(AuthorityClient.Basic(app.Name, secret), redirect, CloudLoginPkce.CreateState(), CloudLoginPkce.CreateChallenge(verifier));
        Assert.True(begun.Status == HttpStatusCode.OK, $"Begin refused: {begun.Status} {begun.Body}");

        browser ??= ecosystem.NewBrowser();

        if (browser.CookieValue(new Uri(AuthorityHost.Origin).Host, "CloudLogin") is null)
            await EcosystemFlow.SignInAtAuthorityOnlyAsync(browser, email);

        (string code, _) = EcosystemFlow.CodeAndState(await EcosystemFlow.CallbackUrlAsync(browser, begun.Property("transaction")!));

        ApiResult redeemed = await client.RedeemAsync(AuthorityClient.Basic(app.Name, secret), code, verifier, redirect);
        Assert.True(redeemed.Status == HttpStatusCode.OK, $"Redeem refused: {redeemed.Status} {redeemed.Body}");

        string access = redeemed.Property("access_token")!;
        string sessionId = new JsonWebToken(access).GetClaim("sid").Value;

        return new TokenSet(access, redeemed.Property("refresh_token"), sessionId, code, verifier, redirect);
    }
}
