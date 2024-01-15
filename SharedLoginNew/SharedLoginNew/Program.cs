using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Providers;
using AngryMonkey.CloudLogin.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddControllers();

builder.Services.AddCors(cors =>
{
    cors.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.Configure<EmailServiceOptions>(builder.Configuration.GetSection("EmailServer"));
IServiceCollection test = builder.Services.AddScoped<EmailService>();


//cloud login -----------
CloudLoginConfiguration cloudLoginConfig = new()
{
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
        new MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft")),
        new GoogleProviderConfiguration(builder.Configuration.GetSection("Google")),
        new WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp"), true),
        new CustomProviderConfiguration(builder.Configuration.GetSection("Custom")),
    ]
};

builder.Services.AddCloudLoginServer(cloudLoginConfig, builder.Configuration);

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});

builder.Services.AddOptions();
builder.Services.AddAuthenticationCore();

builder.Services.AddScoped<CustomAuthenticationStateProvider>();
builder.Services.AddScoped<UserController>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<SharedLoginNew.Client.App>()
    .AddInteractiveWebAssemblyRenderMode();

app.Run();
