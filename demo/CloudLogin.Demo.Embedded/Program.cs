using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.API.Controllers;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using CloudLogin.Demo.Embedded;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(ProvidersController).Assembly);

DemoInboxService demoInbox = new();
DemoInMemoryCloudLoginStore demoStore = new();

await demoStore.Create(new CloudUser
{
    Id = Guid.NewGuid(),
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
builder.Services.AddScoped<DemoAccountRegistryState>();

CloudLoginWebConfiguration loginConfig = new()
{
    WebConfig = web => web.PageDefaults.SetTitle("CloudLogin Developer Demo"),
    Security = new() { EnableLegacyClientVerificationCodes = true },
    Providers =
    [
        new LoginProviders.PasswordProviderConfiguration(builder.Configuration.GetSection("Password")),
        new LoginProviders.CodeProviderConfiguration(builder.Configuration.GetSection("Code")),
        new LoginTestProviders.TestModeConfiguration(builder.Configuration.GetSection("TestMode"))
    ],
    EmailSendCodeRequest = value =>
    {
        demoInbox.Capture(value.Address, value.Code);
        return Task.CompletedTask;
    }
};

builder.Services.AddCloudLoginEmbedded(loginConfig, builder.Configuration);

WebApplication app = builder.Build();

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

app.MapRazorComponents<CloudLogin.Demo.Embedded.App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(AngryMonkey.CloudLogin.WebAssembly._Imports).Assembly);

await app.RunAsync();
