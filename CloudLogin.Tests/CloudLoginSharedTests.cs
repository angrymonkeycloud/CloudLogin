namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginSharedTests
{
    [Theory]
    [InlineData("https://localhost:7116/login", "https://localhost:7116", true)]
    [InlineData("https://localhost:7116/login", "https://localhost:51671", false)]
    [InlineData("http://example.com", "https://example.com", false)]
    [InlineData("https://EXAMPLE.com/path", "https://example.com/other", true)]
    public void IsSameOrigin_ComparesSchemeHostAndPort(string first, string second, bool expected)
        => Assert.Equal(expected, CloudLoginShared.IsSameOrigin(first, second));

    [Fact]
    public void AppendQueryParameter_PreservesExistingQueryAndFragment()
    {
        string result = CloudLoginShared.AppendQueryParameter(
            "https://example.com/callback?state=abc#section",
            "requestId",
            "a b");

        Assert.Equal("https://example.com/callback?state=abc&requestId=a%20b#section", result);
    }

    [Theory]
    [InlineData("/account", true)]
    [InlineData("//attacker.example", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://example.com/callback", true)]
    [InlineData("http://example.com/callback", false)]
    [InlineData("http://localhost:5000/callback", true)]
    public void IsValidRedirectUri_RejectsUnsafeTargets(string target, bool expected)
        => Assert.Equal(expected, CloudLoginShared.IsValidRedirectUri(target));
}
