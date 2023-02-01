using AngryMonkey.Cloud.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Http;
using SharedLogin.WebAssembly;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddCloudWeb(new CloudWebOptions()
{
    DefaultTitle = "Shared Login", // Your app main title
    TitleSuffix = " - Shared Login", // Your app suffix that would be added to a page title if exists
    SiteBundles = new List<CloudBundle>() // Bundles that should be added to the layout
     {
      new CloudBundle(){ Source = "css/app.css", MinOnRelease = false},
     }
});

builder.RootComponents.Add<CloudHeadInit>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Services.AddCloudLogin(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddApiAuthorization();

builder.Services.AddHttpContextAccessor();

await builder.Build().RunAsync();
