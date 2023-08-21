using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SharedLogin.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

HttpClient client = new() { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };

builder.Services.AddScoped(sp => client);

CloudLoginClient cloudLogin = new()
{
    HttpServer = client,
    LoginUrl = builder.HostEnvironment.BaseAddress
};

builder.Services.AddSingleton(sp => cloudLogin);

await builder.Build().RunAsync();
