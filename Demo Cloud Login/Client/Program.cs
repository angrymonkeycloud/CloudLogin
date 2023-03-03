using AngryMonkey.Cloud.Components;
using Demo_Cloud_Login.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddCloudWeb(new CloudWebOptions()
{
    DefaultTitle = "Cloud Login Demo",
    SiteBundles = new List<CloudBundle>()
    {
        new CloudBundle(){ Source = "css/site.css",MinOnRelease=false},
    }
});

builder.RootComponents.Add<CloudHeadInit>("head::after");

await builder.Services.AddCloudLogin(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
