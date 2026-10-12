using AngryMonkey.CloudLogin.Native;

namespace AngryMonkey.CloudLogin.Tests.Native;

public class CloudLoginNativeProtocolTests
{
    private const string Callback = "blusky://auth/callback";
    private static readonly string State = CloudLoginPkce.CreateState();

    private static Dictionary<string, string> Query(string url)
    {
        Dictionary<string, string> values = [];

        foreach (string pair in new Uri(url).Query.TrimStart('?').Split('&'))
        {
            string[] parts = pair.Split('=', 2);
            values[parts[0]] = Uri.UnescapeDataString(parts[1]);
        }

        return values;
    }

    // ── Callback URL ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("blusky", "auth", "/callback", "blusky://auth/callback")]
    [InlineData("myapp", "auth", "callback", "myapp://auth/callback")]
    [InlineData("com.example.app", "signin", "/done", "com.example.app://signin/done")]
    public void CallbackUrl_IsSchemeHostAndPath(string scheme, string host, string path, string expected) =>
        Assert.Equal(expected, CloudLoginNativeProtocol.CallbackUrl(scheme, host, path));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CallbackUrl_NeedsAScheme(string scheme) =>
        Assert.ThrowsAny<ArgumentException>(() => CloudLoginNativeProtocol.CallbackUrl(scheme));

    // ── Login URL ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoginUrl_CarriesTheS256ChallengeOfTheVerifier_NotTheVerifier()
    {
        string verifier = CloudLoginPkce.CreateVerifier();
        string url = CloudLoginNativeProtocol.LoginUrl("https://app.test", verifier, State, Callback);
        Dictionary<string, string> query = Query(url);

        Assert.Equal(CloudLoginPkce.CreateChallenge(verifier), query["challenge"]);
        Assert.DoesNotContain(verifier, url, StringComparison.Ordinal);
        Assert.Equal(State, query["state"]);
        Assert.Equal(Callback, query["redirect_uri"]);
    }

    [Theory]
    [InlineData("https://app.test")]
    [InlineData("https://app.test/")]
    [InlineData("https://app.test///")]
    public void LoginUrl_IsOnTheBackendsNativeLoginPath(string application) =>
        Assert.StartsWith("https://app.test/auth/native/login?", CloudLoginNativeProtocol.LoginUrl(application, CloudLoginPkce.CreateVerifier(), State, Callback));

    [Fact]
    public void LoginUrl_KeepsABasePath() =>
        Assert.StartsWith("https://host.test/tenant/auth/native/login?", CloudLoginNativeProtocol.LoginUrl("https://host.test/tenant", CloudLoginPkce.CreateVerifier(), State, Callback));

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces in it here")]
    [InlineData("has/slash/characters/x")]
    [InlineData("")]
    public void LoginUrl_RefusesAStateTheBackendWouldRefuse(string state) =>
        Assert.Throws<ArgumentException>(() => CloudLoginNativeProtocol.LoginUrl("https://app.test", CloudLoginPkce.CreateVerifier(), state, Callback));

    [Fact]
    public void LoginUrl_NeedsAVerifierAndACallback()
    {
        Assert.ThrowsAny<ArgumentException>(() => CloudLoginNativeProtocol.LoginUrl("https://app.test", "", State, Callback));
        Assert.ThrowsAny<ArgumentException>(() => CloudLoginNativeProtocol.LoginUrl("https://app.test", CloudLoginPkce.CreateVerifier(), State, ""));
        Assert.ThrowsAny<ArgumentException>(() => CloudLoginNativeProtocol.LoginUrl("", CloudLoginPkce.CreateVerifier(), State, Callback));
    }

    [Fact]
    public void EachLoginStartsWithItsOwnStateAndVerifier()
    {
        HashSet<string> urls = [.. Enumerable.Range(0, 50).Select(_ => CloudLoginNativeProtocol.LoginUrl("https://app.test", CloudLoginPkce.CreateVerifier(), CloudLoginPkce.CreateState(), Callback))];
        Assert.Equal(50, urls.Count);
    }

    // ── State ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AGeneratedState_IsValid()
    {
        for (int index = 0; index < 100; index++)
            Assert.True(CloudLoginNativeProtocol.IsValidState(CloudLoginPkce.CreateState()));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0123456789abcde", false)]
    [InlineData("0123456789abcdef", true)]
    [InlineData("abc-DEF_123.xyz~ok", true)]
    [InlineData("abc+def+ghi+jkl+mno", false)]
    [InlineData("abc def ghi jkl mno", false)]
    [InlineData("abc%20def%20ghi%20jk", false)]
    [InlineData("ümlaut-is-not-ascii", false)]
    public void IsValidState_AcceptsSixteenToTwoHundredFiftySixUnreservedCharacters(string? state, bool valid) =>
        Assert.Equal(valid, CloudLoginNativeProtocol.IsValidState(state));

    [Fact]
    public void IsValidState_HasAnUpperBound()
    {
        Assert.True(CloudLoginNativeProtocol.IsValidState(new string('a', 256)));
        Assert.False(CloudLoginNativeProtocol.IsValidState(new string('a', 257)));
    }

    [Theory]
    [InlineData("same-state-value-1", "same-state-value-1", true)]
    [InlineData("same-state-value-1", "same-state-value-2", false)]
    [InlineData("same-state-value-1", "SAME-STATE-VALUE-1", false)]
    [InlineData("same-state-value-1", "same-state-value-1 ", false)]
    [InlineData(null, "same-state-value-1", false)]
    [InlineData("", "same-state-value-1", false)]
    [InlineData("same-state-value-1", null, false)]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    public void StateMatches_OnlyAnExactNonEmptyMatch(string? received, string? expected, bool matches) =>
        Assert.Equal(matches, CloudLoginNativeProtocol.StateMatches(received, expected));

    // ── Reading the callback ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ACallbackWithAHandoff_IsRead()
    {
        Assert.True(CloudLoginNativeProtocol.TryParseCallback($"{Callback}?handoff=abc&state={State}", Callback, out CloudLoginNativeCallback callback));

        Assert.Equal("abc", callback.Handoff);
        Assert.Equal(State, callback.State);
        Assert.Null(callback.Error);
        Assert.False(callback.IsLogout);
    }

    [Fact]
    public void ACallbackWithAnError_IsRead()
    {
        Assert.True(CloudLoginNativeProtocol.TryParseCallback($"{Callback}?error=unavailable&state={State}", Callback, out CloudLoginNativeCallback callback));

        Assert.Equal("unavailable", callback.Error);
        Assert.Null(callback.Handoff);
    }

    [Fact]
    public void ALogoutCallback_IsRead()
    {
        Assert.True(CloudLoginNativeProtocol.TryParseCallback($"{Callback}?operation=logout&state={State}", Callback, out CloudLoginNativeCallback callback));
        Assert.True(callback.IsLogout);
    }

    [Fact]
    public void EscapedValues_AreUnescaped()
    {
        Assert.True(CloudLoginNativeProtocol.TryParseCallback($"{Callback}?handoff=a%2Bb%2Fc%3D&state=x%7Ey", Callback, out CloudLoginNativeCallback callback));

        Assert.Equal("a+b/c=", callback.Handoff);
        Assert.Equal("x~y", callback.State);
    }

    [Fact]
    public void SurroundingWhitespace_IsIgnored() =>
        Assert.True(CloudLoginNativeProtocol.TryParseCallback($"  {Callback}?handoff=abc&state={State} \n", Callback, out _));

    [Theory]
    [InlineData("BLUSKY://AUTH/callback?handoff=abc&state=s")]
    [InlineData("blusky://auth/callback/?handoff=abc&state=s")]
    public void TheSchemeAndHostAreCaseInsensitive_AndATrailingSlashIsAllowed(string url) =>
        Assert.True(CloudLoginNativeProtocol.TryParseCallback(url, Callback, out _));

    [Theory]
    [InlineData("other://auth/callback?handoff=abc&state=s", "another application's scheme")]
    [InlineData("https://auth/callback?handoff=abc&state=s", "a web address")]
    [InlineData("blusky://evil/callback?handoff=abc&state=s", "another host")]
    [InlineData("blusky://auth/elsewhere?handoff=abc&state=s", "another path")]
    [InlineData("blusky://auth/Callback?handoff=abc&state=s", "a differently cased path")]
    [InlineData("blusky://auth/callback/extra?handoff=abc&state=s", "a longer path")]
    public void ACallbackOnAnyOtherAddress_IsNotRead(string url, string reason)
    {
        Assert.False(CloudLoginNativeProtocol.TryParseCallback(url, Callback, out _), reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/callback?handoff=abc")]
    [InlineData("blusky://auth/callback")]
    [InlineData("blusky://auth/callback?")]
    [InlineData("blusky://auth/callback?state=only")]
    [InlineData("blusky://auth/callback?handoff=&state=s")]
    [InlineData("blusky://auth/callback?operation=other&state=s")]
    public void ACallbackWithNothingToAct_On_IsNotRead(string? url) =>
        Assert.False(CloudLoginNativeProtocol.TryParseCallback(url, Callback, out _));

    [Fact]
    public void AnInvalidExpectedCallback_ReadsNothing() =>
        Assert.False(CloudLoginNativeProtocol.TryParseCallback($"{Callback}?handoff=abc", "not a url", out _));

    [Fact]
    public void AParameterGivenTwice_KeepsTheFirst()
    {
        Assert.True(CloudLoginNativeProtocol.TryParseCallback($"{Callback}?handoff=first&handoff=second&state=s", Callback, out CloudLoginNativeCallback callback));
        Assert.Equal("first", callback.Handoff);
    }

    [Fact]
    public void ParameterNames_AreCaseSensitive()
    {
        Assert.False(CloudLoginNativeProtocol.TryParseCallback($"{Callback}?HANDOFF=abc&state=s", Callback, out _));
    }

    [Fact]
    public void UnknownParameters_AreIgnored()
    {
        Assert.True(CloudLoginNativeProtocol.TryParseCallback($"{Callback}?utm=x&handoff=abc&state=s&extra", Callback, out CloudLoginNativeCallback callback));
        Assert.Equal("abc", callback.Handoff);
    }

    [Fact]
    public void AFailedParse_LeavesAnEmptyCallback()
    {
        Assert.False(CloudLoginNativeProtocol.TryParseCallback("other://x/y?handoff=abc", Callback, out CloudLoginNativeCallback callback));
        Assert.Null(callback.Handoff);
        Assert.Null(callback.State);
    }

    [Fact]
    public void ThePathsTheAppAndTheCliCall_AreTheBackendsNativeEndpoints()
    {
        Assert.Equal("auth/native/login", CloudLoginNativeProtocol.LoginPath);
        Assert.Equal("auth/native/exchange", CloudLoginNativeProtocol.ExchangePath);
        Assert.Equal("auth/native/logout", CloudLoginNativeProtocol.SessionEndPath);
        Assert.Equal("auth/token", CloudLoginNativeProtocol.TokenPath);
    }
}
