using AngryMonkey.CloudCommon.Theming;
using AngryMonkey.CloudLogin.Server;
using Xunit;

namespace CloudLogin.Tests;

public class ThemeAppearanceTests
{
    [Fact]
    public void DefaultThemePreservesLoginBrandAndFields()
    {
        CloudLoginWebConfiguration configuration = new();
        ResolvedTheme resolved = ThemeResolver.Resolve(configuration.ResolveTheme());
        Assert.Equal("#0078d4", resolved.Tokens["primary-500"]);
        Assert.Equal("#8a8886", resolved.Tokens["color-control-border"]);
        Assert.Contains("Segoe UI", resolved.Tokens["font-family"]);
    }

    [Fact]
    public void CompleteThemeTakesPrecedenceOverLoginDefaults()
    {
        ThemeDefinition custom = CloudThemes.Color("#ff9100");
        CloudLoginWebConfiguration configuration = new() { Theme = custom };
        Assert.Same(custom, configuration.ResolveTheme());
    }
}
