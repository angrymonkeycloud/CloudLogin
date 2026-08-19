using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using CloudLogin.Demo;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

DemoInboxService demoInbox = new();
DemoInMemoryCloudLoginStore demoStore = new();

// Seeded so the Admin tab in the Account UI (gated by UserModel.IsGlobalAdmin) is reachable
// without any manual setup: pick "Demo Admin" from the Test Mode user list on the login screen.
await demoStore.Create(new UserModel
{
    ID = Guid.NewGuid(),
    FirstName = "Demo",
    LastName = "Admin",
    DisplayName = "Demo Admin (Global Admin)",
    IsTest = true,
    IsGlobalAdmin = true,
    CreatedOn = DateTimeOffset.UtcNow,
    Inputs = [new LoginInput { Input = "admin@demo.cloudlogin", Format = InputFormat.EmailAddress, IsPrimary = true }]
});

builder.Services.AddSingleton(demoInbox);
builder.Services.AddSingleton<ICloudLoginStore>(demoStore);
builder.Services.AddCloudLoginAccountRegistry();
builder.Services.AddSingleton<DemoAccountRegistrySeed>();

builder.AddCloudLoginWeb(options =>
{
    options.WebConfig = web => web.PageDefaults.SetTitle("CloudLogin Demo - Authority");

    // The Code provider uses the legacy client-managed verification-code flow, which is
    // disabled by default in production. It's safe to opt in here since this demo only
    // ever runs in Development.
    options.Security.EnableLegacyClientVerificationCodes = true;
    options.EnableLegacyClientManagedLogin = true;

    options.Providers =
    [
        new LoginProviders.PasswordProviderConfiguration(builder.Configuration.GetSection("Password")),
        new LoginProviders.CodeProviderConfiguration(builder.Configuration.GetSection("Code")),
        new LoginTestProviders.TestModeConfiguration(builder.Configuration.GetSection("TestMode"))
    ];

    options.EmailSendCodeRequest = value =>
    {
        demoInbox.Capture(value.Address, value.Code);
        return Task.CompletedTask;
    };

    // Matches the fixed port used by demo/CloudLogin.Demo.Consumer.
    options.AllowWebsite("https://localhost:7200");
});

WebApplication app = builder.Build();
DemoAccountRegistrySeed accountSeed = app.Services.GetRequiredService<DemoAccountRegistrySeed>();
await accountSeed.InitializeAsync();

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.MapStaticAssets();
app.UseRouting();
app.UseCloudLoginSecurity();
app.UseAuthentication();
app.UseAntiforgery();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/demo/inbox", (DemoInboxService inbox) => Results.Json(inbox.GetRecent()));

app.MapRazorComponents<CloudLogin.Demo.App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(AngryMonkey.CloudLogin.Main.App).Assembly, typeof(AngryMonkey.CloudLogin.WebAssembly._Imports).Assembly);

await app.RunAsync();
