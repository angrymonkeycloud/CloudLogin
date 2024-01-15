using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSingleton(CloudLoginStandaloneClient.Build(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();

