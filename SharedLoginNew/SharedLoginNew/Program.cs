using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Providers;
using Microsoft.AspNetCore.Authentication.Cookies;
using SharedLoginNew.Client.Pages;
using SharedLoginNew.Components;
using System.Net;
using System.Net.Mail;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// -------------------- for cloud login


builder.Services.AddControllers();

builder.Services.AddCors(cors =>
{
    cors.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
//cloud login -----------
CloudLoginConfiguration cloudLoginConfig = new()
{
    Cosmos = new CosmosDatabase(builder.Configuration.GetSection("Cosmos")),
    LoginDuration = new TimeSpan(30, 0, 0, 0),
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
        new WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp"), true)
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


// -------------------- for cloud login


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// -------------------- for cloud login
app.UseRouting();
app.UseCloudLogin();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// -------------------- for cloud login

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Counter).Assembly);

app.Run();
