using AngryMonkey.CloudWeb;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Http;
using SharedLogin.WebAssembly;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddCloudWeb(new()
{
    PageDefaults = new()
    {
        Title = "Shared Login", // Your app main title
        CallingAssemblyName = "SharedLogin.WebAssembly",
        AutoAppendBlazorStyles = true,
        FollowPage = false,
        IndexPage = false,
        Bundles = new List<CloudBundle>() // Bundles that should be added to the layout
            {
                new CloudBundle(){ Source = "css/site.css", MinOnRelease = false},
            }
    },
    TitleSuffix = " - Shared Login", // Your app suffix that would be added to a page title if exists
});

builder.RootComponents.Add<CloudHeadInit>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Services.AddCloudLogin(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddApiAuthorization();

builder.Services.AddHttpContextAccessor();

await builder.Build().RunAsync();
