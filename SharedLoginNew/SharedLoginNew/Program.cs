using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Providers;
using AngryMonkey.CloudLogin.Services;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Net;
using System.Net.Mail;
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
    EmailSendCodeRequest = async (sendCode) =>
    {
        //SmtpClient smtpClient = new(builder.Configuration["SMTP:Host"], int.Parse(builder.Configuration["SMTP:Port"]!))
        //{
        //    EnableSsl = true,
        //    DeliveryMethod = SmtpDeliveryMethod.Network,
        //    UseDefaultCredentials = false,
        //    Credentials = new NetworkCredential(builder.Configuration["SMTP:Email"], builder.Configuration["SMTP:Password"])
        //};

        StringBuilder mailBody = new();
        mailBody.AppendLine("<div style=\"width:300px;margin:20px auto;padding: 15px;border:1px dashed  #4569D4;text-align:center\">");
        mailBody.AppendLine("<h3>Hello,</h3>");
        mailBody.AppendLine("<p>We recevied a request to login page.</p>");
        mailBody.AppendLine("<p style=\"margin-top: 0;\">Enter the following password login code:</p>");
        mailBody.AppendLine("<div style=\"width:150px;border:1px solid #4569D4;margin: 0 auto;padding: 10px;text-align:center;\">");
        mailBody.AppendLine($"<b style=\"color:#202124;text-decoration:none\">{sendCode.Code}</b> <br />");
        mailBody.AppendLine("</div></div>");


        ServiceProvider serviceProvider = builder.Services.BuildServiceProvider();
        EmailService emailService = serviceProvider.GetService<EmailService>();

        await emailService.SendEmail("Login Code", mailBody.ToString(), [sendCode.Address]);
    },
    Providers =
    [
        new MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft")),
        new GoogleProviderConfiguration(builder.Configuration.GetSection("Google")),
        new WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp"), true),
        new CustomProviderConfiguration(builder.Configuration.GetSection("Custom")),
    ]
};

builder.Services.AddCloudLoginServer(cloudLoginConfig);

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
