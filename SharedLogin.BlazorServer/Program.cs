using AngryMonkey.Cloud.Components;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Controllers;
using AngryMonkey.CloudLogin.DataContract;
using AngryMonkey.CloudLogin.Providers;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Net;
using System.Net.Mail;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();


CloudLoginConfiguration cloudLoginConfig = new()
{
    Cosmos = new CosmosDatabase()
    {
        ConnectionString = builder.Configuration["Cosmos:ConnectionString"],
        DatabaseId = builder.Configuration["Cosmos:DatabaseId"],
        ContainerId = builder.Configuration["Cosmos:ContainerId"],
        RequestContainerId = builder.Configuration["Cosmos:RequestContainerId"],
    },
    FooterLinks = new List<Link>()
    {
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
        mailBody.AppendLine("<div style=\"width:300px;margin:20px auto;padding: 15px;border:1px dashed  #4569D4;text-align:center\">");
        mailBody.AppendLine("<h3>Hello,</h3>");
        mailBody.AppendLine("<p>We recevied a request to login page.</p>");
        mailBody.AppendLine("<p style=\"margin-top: 0;\">Enter the following password login code:</p>");
        mailBody.AppendLine("<div style=\"width:150px;border:1px solid #4569D4;margin: 0 auto;padding: 10px;text-align:center;\">");
        mailBody.AppendLine($"code: <b style=\"color:#202124;text-decoration:none\">{sendCode.Code}</b> <br />");
        mailBody.AppendLine("</div></div>");

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
    Providers = new List<ProviderConfiguration>()
    {
        new MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft")),
        new GoogleProviderConfiguration(builder.Configuration.GetSection("Google")),
        new FacebookProviderConfiguration(builder.Configuration.GetSection("Facebook")),
        new TwitterProviderConfiguration(builder.Configuration.GetSection("Twitter")),
        new WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp")),
        new CustomProviderConfiguration(builder.Configuration.GetSection("Custom"))
    }
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

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();
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