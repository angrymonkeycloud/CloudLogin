using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using CloudLogin.Demo;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

DemoInboxService demoInbox = new();
DemoInMemoryCloudLoginStore demoStore = new();

// Seeded so the Admin tab in the Account UI (gated by CloudUser.IsGlobalAdmin) is reachable
// without any manual setup: pick "Demo Admin" from the Test Mode user list on the login screen.
// The account registry seeds against this same id, so the workspaces, subscriptions, and
// billing below all belong to the account you sign in as.
Guid demoAdminUserId = Guid.NewGuid();

await demoStore.Create(new CloudUser
{
    ID = demoAdminUserId,
    FirstName = "Demo",
    LastName = "Admin",
    DisplayName = "Demo Admin (Global Admin)",
    IsTest = true,
    IsGlobalAdmin = true,
    CreatedOn = DateTimeOffset.UtcNow,
    Inputs = [new CloudLoginInput { Input = "admin@demo.cloudlogin", Format = CloudLoginInputFormat.EmailAddress, IsPrimary = true }]
});

builder.Services.AddSingleton(demoInbox);
builder.Services.AddSingleton<ICloudLoginStore>(demoStore);
builder.Services.AddCloudLoginAccountRegistry();
builder.Services.AddSingleton(services => new DemoAccountRegistrySeed(services.GetRequiredService<IServiceScopeFactory>(), demoAdminUserId));

builder.AddCloudLoginWeb(options =>
{
    options.WebConfig = web => web.PageDefaults.SetTitle("CloudLogin Demo - Authority");

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

    // Both caps are optional; omit them for the defaults (3 owned, 10 memberships).
    // SingularLabel/PluralLabel show the concept as "Business"/"Businesses" here —
    // everything internal (routes, JSON, webhook events) still says "Workspace".
    options.Workspace = new()
    {
        MaxOwnedPerUser = 3,
        MaxPerUser = 6,
        SingularLabel = "Business",
        PluralLabel = "Businesses"
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
