using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Services;
using AngryMonkey.CloudLogin.Sever.Providers;

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddCors(cors =>
//{
//    cors.AddPolicy("AllowAll", policy =>
//        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
//});

builder.Services.Configure<EmailServiceOptions>(builder.Configuration.GetSection("SMTP"));
IServiceCollection test = builder.Services.AddScoped<EmailService>();


//cloud login -----------
CloudLoginConfiguration cloudLoginConfig = new()
{
    WebConfig = config =>
    {
        config.PageDefaults.SetTitle("Test login");
    },
    Cosmos = new CosmosConfiguration(builder.Configuration.GetSection("Cosmos")),
    LoginDuration = new TimeSpan(30, 0, 0, 0),
    EmailConfiguration = new()
    {
        EmailService = builder.Services.BuildServiceProvider().GetRequiredService<EmailService>()
    },
    FooterLinks =
    [
        new Link()
        {
            Title = "Link 1",
            Url = "#"
        },
        new Link()
        {
            Title = "Link 2",
            Url = "#"
        }
    ],
    //EmailSendCodeRequest = async (sendCode) =>
    //{
    //    StringBuilder mailBody = new();
    //    mailBody.AppendLine("");
    //    mailBody.AppendLine("");
    //    mailBody.AppendLine("");
    //    mailBody.AppendLine("");
    //    mailBody.AppendLine("");
    //    mailBody.AppendLine($"");
    //    mailBody.AppendLine("");


    //    ServiceProvider serviceProvider = builder.Services.BuildServiceProvider();
    //    EmailService? emailService = serviceProvider.GetService<EmailService>();

    //    await emailService.SendEmail("Login Code", mailBody.ToString(), [sendCode.Address]);
    //},
    Providers =
    [
        new MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft")){ Audience = MicrosoftProviderAudience.Personal },
        new GoogleProviderConfiguration(builder.Configuration.GetSection("Google")),
        //new WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp"), true),
        //new CustomProviderConfiguration(builder.Configuration.GetSection("Custom")),
    ]
};

builder.Services.AddCloudLoginWeb(cloudLoginConfig, builder.Configuration);

await CloudLoginWeb.InitApp(builder);