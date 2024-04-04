using AngryMonkey.CloudLogin;
using StandaloneDemo.Components;

var builder = WebApplication.CreateBuilder(args);

//await builder.Services.AddCloudLoginMVC("https://login.coverbox.app/");
await builder.Services.AddCloudLoginMVC("https://localhost:7003/");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseRouting();
app.MapControllers();
app.UseAuthentication();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(StandaloneDemo.Client._Imports).Assembly);

app.UseCloudLoginHandler();
//app.CloudLoginAutomatically();

app.Run();
