using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Controllers;
using AngryMonkey.CloudLogin.DataContract;
using AngryMonkey.CloudLogin.Providers;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Net.Mail;
using System.Net;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

CloudLoginConfiguration cloudLoginConfig = new()
{
    Cosmos = new CosmosDatabase(builder.Configuration.GetSection("Cosmos")),
    Providers = new List<ProviderConfiguration>()
    {
        new MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft")),
        new GoogleProviderConfiguration(builder.Configuration.GetSection("Google")),
        new FacebookProviderConfiguration(builder.Configuration.GetSection("Facebook")),
        new TwitterProviderConfiguration(builder.Configuration.GetSection("Twitter")),
        new WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp")),
        new CustomProviderConfiguration(builder.Configuration.GetSection("Custom"))
    },
    FooterLinks = new List<Link>()
    {
        new Link()
        {
            Title = "POST License",
            Url = "https://angrymonkeycloud.com/POSTLicense.txt"
        },
        new Link()
        {
            Title = "Cloud Components",
            Url = "https://angrymonkeycloud.com/cloudcomponents"
        }
    },
    EmailSendCodeRequest = async (sendCode) =>
    {
        SmtpClient smtpClient = new(builder.Configuration["SMTP:Host"], int.Parse(builder.Configuration["SMTP:Port"]))
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(builder.Configuration["SMTP:Email"], builder.Configuration["SMTP:Password"])
        };
        StringBuilder mailBody = new();
        mailBody.AppendLine($"code: <b style=\"color:#202124;text-decoration:none\">{sendCode.Code}</b> <br />");
        MailMessage mailMessage = new()
        {
            From = new MailAddress(builder.Configuration["SMTP:Email"], "Cloud Login"),
            Subject = "Login Code",
            IsBodyHtml = true,
            Body = mailBody.ToString()
        };
        mailMessage.To.Add(sendCode.Address);
        await smtpClient.SendMailAsync(mailMessage);
    },
    LoginDuration = new TimeSpan(24,0,0)

};

builder.Services.AddCloudLoginServer(cloudLoginConfig);

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});

builder.Services.AddOptions();
builder.Services.AddAuthenticationCore();

builder.Services.AddScoped<CustomAuthenticationStateProvider>();
builder.Services.AddScoped(key => new UserController());


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseCloudLogin();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
