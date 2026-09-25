using AngryMonkey.CloudCommon.Theming;
using AngryMonkey.CloudLogin.Server;

namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginThemeConfigurationTests
{
    [Fact]
    public void FullThemeTakesPrecedenceOverUnusedAccentShorthand()
    {
        CloudLoginWebConfiguration configuration = new()
        {
            PrimaryColor = "unused",
            Theme = CloudThemes.Color("#123456")
        };
        CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false);
    }

    [Fact]
    public void InvalidThemeFailsDuringConfigurationValidation()
    {
        CloudLoginWebConfiguration configuration = new() { Theme = CloudThemes.Color("invalid") };
        Assert.Throws<ArgumentException>(() => CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));
    }

    [Fact]
    public void UnknownThemeModeFailsDuringConfigurationValidation()
    {
        CloudLoginWebConfiguration configuration = new() { ThemeMode = (ThemeModes)99 };
        Assert.Throws<InvalidOperationException>(() => CloudLoginConfigurationValidator.Validate(configuration, isDevelopment: false));
    }
}
