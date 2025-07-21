using AngryMonkey.CloudLogin.Sever.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.AddCloudLoginWeb(new()
{
    WebConfig = config => { config.PageDefaults.SetTitle("Standalone Login Demo"); },

    Cosmos = new(builder.Configuration.GetSection("Cosmos")),
    Logo = "https://guidelines.meloncut.com/brands/angrymonkeyagency/assets/wordmark-logo.svg",

    Providers =
    [
        new LoginProviders.CustomProviderConfiguration(builder.Configuration.GetSection("Custom")),
        new LoginProviders.PasswordProviderConfiguration(builder.Configuration.GetSection("Password")),
        new LoginProviders.GoogleProviderConfiguration(builder.Configuration.GetSection("Google"))
    ]
});

await CloudLoginWeb.InitApp(builder);