using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Net.Mail;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Providers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
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
        ////SmtpClient smtpClient = new(builder.Configuration["SMTP:Host"], int.Parse(builder.Configuration["SMTP:Port"]!))
        ////{
        ////    EnableSsl = true,
        ////    DeliveryMethod = SmtpDeliveryMethod.Network,
        ////    UseDefaultCredentials = false,
        ////    Credentials = new NetworkCredential(builder.Configuration["SMTP:Email"], builder.Configuration["SMTP:Password"])
        ////};

        //StringBuilder mailBody = new();
        //mailBody.AppendLine("<div style=\"width:300px;margin:20px auto;padding: 15px;border:1px dashed  #4569D4;text-align:center\">");
        //mailBody.AppendLine("<h3>Hello,</h3>");
        //mailBody.AppendLine("<p>We recevied a request to login page.</p>");
        //mailBody.AppendLine("<p style=\"margin-top: 0;\">Enter the following password login code:</p>");
        //mailBody.AppendLine("<div style=\"width:150px;border:1px solid #4569D4;margin: 0 auto;padding: 10px;text-align:center;\">");
        //mailBody.AppendLine($"code: <b style=\"color:#202124;text-decoration:none\">{sendCode.Code}</b> <br />");
        //mailBody.AppendLine("</div></div>");

        ////MailMessage mailMessage = new()
        ////{
        ////    From = new MailAddress(builder.Configuration["SMTP:Email"]!, "Cloud Login"),
        ////    Subject = "Login Code",
        ////    IsBodyHtml = true,
        ////    Body = mailBody.ToString()
        ////};

        ////mailMessage.To.Add(sendCode.Address);

        ////await smtpClient.SendMailAsync(mailMessage);

        //EmailServiceOptions emailOptions = new(builder.Configuration.GetSection("EmailServer"));

        //string[] _scopes = ["https://graph.microsoft.com/.default"];
        //ClientSecretCredential clientSecretCredential = new(emailOptions.TenantId, emailOptions.ClientId, emailOptions.Secret);
        //GraphServiceClient _graphServiceClient = new GraphServiceClient(clientSecretCredential, _scopes);

        //Message message = new()
        //{
        //    Subject = "Login Code",
        //    Body = new ItemBody
        //    {
        //        ContentType = BodyType.Html,
        //        Content = mailBody.ToString()
        //    },
        //    ToRecipients =
        //    [
        //        new()
        //        {
        //            EmailAddress = new()
        //            {
        //                Address = sendCode.Address
        //            }
        //        }
        //    ],
        //    From = new() { EmailAddress = new() { Address = emailOptions.FromEmail } },
        //};

        //await _graphServiceClient.Users["elietebchrani@coverbox.app"].SendMail.PostAsync(new()
        //{
        //    Message = message,
        //    SaveToSentItems = true
        //});
    },
    Providers =
    [
        new MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft")),
        new GoogleProviderConfiguration(builder.Configuration.GetSection("Google")),
        new WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp"), true)
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
builder.Services.AddScoped(key => new UserController());


// -------------------- for cloud login


builder.Services.AddHttpContextAccessor();


builder.Services.AddOptions();
builder.Services.AddAuthenticationCore();

builder.Services.AddScoped<CustomAuthenticationStateProvider>();
builder.Services.AddScoped(key => new UserController());

builder.Services.AddScoped<CustomAuthenticationStateProvider>();
builder.Services.AddScoped(key => new UserController());

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
}


app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseCloudLogin();
app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapControllers();
app.MapFallbackToPage("/_Host");

app.Run();
