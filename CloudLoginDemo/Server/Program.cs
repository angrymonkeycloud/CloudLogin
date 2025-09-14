using AngryMonkey.Cloud.Login.Controllers;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.ResponseCompression;
using System.Net;
using System.Net.Mail;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

CloudLoginConfiguration cloudLoginConfig = new()
{
    // IMPORTANT: This is the URL where your CloudLogin service is hosted
    // This should be the domain/URL where this CloudLogin application is running
    // For example:
    // - Development: "https://localhost:7125" 
    // - Production: "https://login.yourcompany.com"
    // - NOT the URL of the calling application
    BaseAddress = "https://localhost:7125", // This should match where THIS CloudLogin service runs
    
	Cosmos = new CosmosDatabase()
	{
		ConnectionString = builder.Configuration["Cosmos:ConnectionString"],
		DatabaseId = builder.Configuration["Cosmos:DatabaseId"],
		ContainerId = builder.Configuration["Cosmos:ContainerId"]
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
		new MicrosoftProviderConfiguration()
		{
			ClientId = builder.Configuration["Microsoft:ClientId"],
			ClientSecret= builder.Configuration["Microsoft:ClientSecret"],
		},
		new GoogleProviderConfiguration()
		{
			ClientId = builder.Configuration["Google:ClientId"],
			ClientSecret= builder.Configuration["Google:ClientSecret"]
		},
		new FacebookProviderConfiguration()
		{
			ClientId = builder.Configuration["Facebook:ClientId"],
			ClientSecret= builder.Configuration["Facebook:ClientSecret"]
		},
		new TwitterProviderConfiguration()
		{
			ClientId = builder.Configuration["Twitter:ClientId"],
			ClientSecret= builder.Configuration["Twitter:ClientSecret"]
		},
		new WhatsAppProviderConfiguration()
		{
			RequestUri = builder.Configuration["WhatsApp:RequestUri"],
			Authorization = builder.Configuration["WhatsApp:Authorization"],
			Template = "testcode",
			Language = "en"
		}
	}
};

builder.Services.AddCloudLoginServer(cloudLoginConfig);

builder.Services.AddAuthentication(opt =>
{
	opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});

builder.Services.AddOptions();
builder.Services.AddAuthenticationCore();

builder.Services.AddSingleton<CustomAuthenticationStateProvider>();
builder.Services.AddSingleton(key => new UserController());

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

app.UseCloudLogin();

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");
// Example API endpoints showing how to use the new URL generation methods
app.MapGet("/api/demo/web-login-url", (IServiceProvider services) =>
{
    var cloudLogin = services.GetRequiredService<ICloudLogin>();
    
    // Generate a login URL for web application
    // This will generate: https://localhost:7125/?redirectUri=https%3A%2F%2Fmyapp.com%2Fdashboard
    string webLoginUrl = cloudLogin.GetLoginUrl("https://myapp.com/dashboard", false);
    
    return new { url = webLoginUrl, type = "web", explanation = "This URL points to the CloudLogin service, not your app" };
});

app.MapGet("/api/demo/mobile-login-url", (IServiceProvider services) =>
{
    var cloudLogin = services.GetRequiredService<ICloudLogin>();
    
    // Generate a login URL for mobile application
    // This will generate: https://localhost:7125/?redirectUri=myapp%3A%2F%2Flogin-success&isMobileApp=true
    string mobileLoginUrl = cloudLogin.GetLoginUrl("myapp://login-success", true);
    
    return new { url = mobileLoginUrl, type = "mobile", explanation = "This URL points to the CloudLogin service with mobile flag" };
});

app.MapGet("/api/demo/provider-login-url", (IServiceProvider services, string provider, string? returnUrl, bool isMobile = false) =>
{
    var cloudLogin = services.GetRequiredService<ICloudLogin>();
    
    try 
    {
        // Generate a provider-specific login URL
        // This will generate: https://localhost:7125/cloudlogin/login/google?redirectUri=https%3A%2F%2Fmyapp.com%2Fdashboard
        string providerLoginUrl = cloudLogin.GetProviderLoginUrl(provider, returnUrl, isMobile, false);
        
        return Results.Ok(new { 
            url = providerLoginUrl, 
            provider = provider, 
            type = isMobile ? "mobile" : "web",
            explanation = $"This URL will initiate {provider} OAuth flow, then return user to {returnUrl}"
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/demo/test-configuration", (IServiceProvider services) =>
{
    var cloudLogin = services.GetRequiredService<ICloudLogin>();
    
    return new { 
        loginUrl = cloudLogin.LoginUrl,
        message = "This should show your CloudLogin service URL (e.g., https://localhost:7125), NOT your calling application URL"
    };
});

app.Run();


/*
OAUTH PROVIDER CONFIGURATION:

In your OAuth provider settings (Google Console, Microsoft Azure, etc.), configure the redirect URI as:
https://localhost:7125/cloudlogin/result  (for development)
https://login.yourcompany.com/cloudlogin/result  (for production)

This URL must match your CloudLogin service BaseAddress + /cloudlogin/result

USAGE EXAMPLES:

1. Web Application Login:
   GET /api/demo/web-login-url
   Returns URL that points to CloudLogin service: https://localhost:7125/?redirectUri=https%3A%2F%2Fmyapp.com%2Fdashboard

2. Mobile Application Login:
   GET /api/demo/mobile-login-url
   Returns URL that points to CloudLogin service: https://localhost:7125/?redirectUri=myapp%3A%2F%2Flogin-success&isMobileApp=true

3. Provider-Specific Login:
   GET /api/demo/provider-login-url?provider=google&returnUrl=https://myapp.com/dashboard&isMobile=false
   Returns URL that points to CloudLogin service: https://localhost:7125/cloudlogin/login/google?redirectUri=https%3A%2F%2Fmyapp.com%2Fdashboard

4. Test Configuration:
   GET /api/demo/test-configuration
   Returns the current LoginUrl to verify it's pointing to CloudLogin service

FLOW EXPLANATION:

1. User clicks login button on https://myapp.com
2. Your app generates URL: https://localhost:7125/cloudlogin/login/google?redirectUri=https%3A%2F%2Fmyapp.com%2Fdashboard
3. User is redirected to CloudLogin service at https://localhost:7125
4. CloudLogin initiates OAuth with Google, using fixed redirect URI: https://localhost:7125/cloudlogin/result
5. Google redirects back to CloudLogin: https://localhost:7125/cloudlogin/result?code=abc123
6. CloudLogin processes OAuth result and redirects user to your app: https://myapp.com/dashboard?requestId=xyz

The key fix: BaseAddress must be set to where your CloudLogin service is hosted, not where your calling application is hosted.
*/
