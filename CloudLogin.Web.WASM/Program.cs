using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient() { BaseAddress = new(builder.HostEnvironment.BaseAddress) });

builder.Services.AddAuthenticationProcessService();
//builder.Services.AddSingleton(await CloudLoginClient.Build(builder.HostEnvironment.BaseAddress));

builder.Services.AddCloudLogin(builder.HostEnvironment.BaseAddress);

await builder.Build().RunAsync();