using System.Web;

namespace AngryMonkey.CloudLogin.Tests;

public class RedirectBoundaryTests
{
    public static TheoryData<string> UnsafeDestinations => new()
    {
        "/\\evil.example/path", "//evil.example/path", "/\t/evil.example", "/\r\nLocation: https://evil.example",
        "https://portal.example.evil.example", "https://portal.example@evil.example", "http://portal.example",
        "https://portal.example:444", "javascript:alert(1)", "data:text/html,hello", "file:///etc/passwd", "blob:https://portal.example/id"
    };

    [Theory]
    [MemberData(nameof(UnsafeDestinations))]
    public async Task AuthenticationRedirectCannotEscapeExactAllowlist(string destination)
    {
        LoginTestFixture fixture = new(allowedOrigins: ["https://portal.example"]);
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Server.CompleteLoginRedirect(destination));
        Assert.Empty(fixture.Store.Requests);
    }

    [Theory]
    [InlineData("/\\evil.example")]
    [InlineData("/\tevil.example")]
    [InlineData("/\r\nevil.example")]
    public void SharedValidationRejectsBrowserNormalizedRedirects(string destination) =>
        Assert.False(CloudLoginShared.IsValidRedirectUri(destination));

    [Theory]
    [InlineData("/Account")]
    [InlineData("/Account?tab=security#password")]
    [InlineData("https://login.example/Account")]
    public async Task LocalAccountNavigationDoesNotCreateHandoff(string destination)
    {
        LoginTestFixture fixture = new();
        fixture.AuthenticateAs(await fixture.AddPasswordUserAsync());
        Assert.Equal(destination, await fixture.Server.CompleteLoginRedirect(destination));
        Assert.Empty(fixture.Store.Requests);
    }

    [Theory]
    [InlineData("a&admin=true")]
    [InlineData("#fragment")]
    [InlineData("https://example.com/a?b=c&d=e")]
    [InlineData("عربي + space")]
    [InlineData("%2F%26")]
    public void QueryValuesRoundTripWithoutInjectingParameters(string value)
    {
        Uri uri = new(CloudLoginShared.AppendQueryParameter("https://example.com/path?existing=1#section", "returnUrl", value));
        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(value, query["returnUrl"]);
        Assert.Equal("1", query["existing"]);
        Assert.Equal(2, query.Count);
        Assert.Equal("#section", uri.Fragment);
    }

    [Theory]
    [InlineData("https://EXAMPLE.com:443/path", "https://example.com", true)]
    [InlineData("https://example.com", "http://example.com", false)]
    [InlineData("https://example.com:444", "https://example.com", false)]
    [InlineData("https://example.com.evil.test", "https://example.com", false)]
    [InlineData("https://example.com@evil.test", "https://example.com", false)]
    [InlineData("/relative", "https://example.com", false)]
    [InlineData(null, null, false)]
    public void OriginComparisonUsesSchemeHostAndEffectivePort(string? first, string? second, bool expected) =>
        Assert.Equal(expected, CloudLoginShared.IsSameOrigin(first, second));
}