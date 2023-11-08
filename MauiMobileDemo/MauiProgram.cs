using AngryMonkey.CloudLogin;
using MauiMobileDemo.Data;
using Microsoft.Extensions.Logging;

namespace MauiMobileDemo
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif
            var client = new HttpClient
            {
                BaseAddress = new Uri("https://login.coverbox.app/")
            };
            CloudLoginClient cloudLoginClient = CloudLoginClient.InitializeForClient(client.BaseAddress.AbsoluteUri);

            builder.Services.AddSingleton(cloudLoginClient);
            builder.Services.AddSingleton(new AccountService());

            return builder.Build();
        }
    }
}