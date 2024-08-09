using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient() { BaseAddress = new(builder.HostEnvironment.BaseAddress) });

builder.Services.AddSingleton(await CloudLoginClient.Build(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();