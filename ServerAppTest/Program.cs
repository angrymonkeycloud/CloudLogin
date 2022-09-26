using AngryMonkey.Cloud.Login;
using AngryMonkey.Cloud.Login.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos.Core;
using ServerAppTest.Controllers;
using System.Net;
using System.Net.Mail;
using System.Text;
using Twilio;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddCloudWeb(new CloudWebOptions()
{
	TitlePrefix = "Cloud Login"
});

builder.Services.AddCloudLogin(new CloudLoginConfiguration()
{
	Cosmos = new CloudLoginConfiguration.CosmosDatabase()
	{
		ConnectionString = builder.Configuration["Cosmos:ConnectionString"],
		DatabaseId = builder.Configuration["Cosmos:DatabaseId"],
		ContainerId = builder.Configuration["Cosmos:ContainerId"]
	},

	EmailSendCodeRequest = async (sendCode) =>
	{
		SmtpClient smtpClient = new("smtp.gmail.com", 587)
		{
			EnableSsl = true,
			DeliveryMethod = SmtpDeliveryMethod.Network,
			UseDefaultCredentials = false,
			Credentials = new NetworkCredential("wissamfarhat51@gmail.com", "ycqirwqugebkxfmh")
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
			From = new MailAddress("wissamfarhat51@gmail.com", "Cloud Login"),
			Subject = "Login Code",
			IsBodyHtml = true,
			Body = mailBody.ToString()
		};

		mailMessage.To.Add(sendCode.Address);

		await smtpClient.SendMailAsync(mailMessage);
	},

	Providers = new List<CloudLoginConfiguration.Provider>()
	{
		new CloudLoginConfiguration.MicrosoftAccount()
		{
			ClientId = builder.Configuration["Microsoft:ClientId"],
			ClientSecret= builder.Configuration["Microsoft:ClientSecret"],
		},
		new CloudLoginConfiguration.GoogleAccount()
		{
			ClientId = builder.Configuration["Google:ClientId"],
			ClientSecret= builder.Configuration["Google:ClientSecret"]
		},
		new CloudLoginConfiguration.FacebookAccount()
		{
			ClientId = builder.Configuration["Facebook:ClientId"],
			ClientSecret= builder.Configuration["Facebook:ClientSecret"]
		},
		new CloudLoginConfiguration.TwitterAccount()
		{
			ClientId = builder.Configuration["Twitter:ClientId"],
			ClientSecret= builder.Configuration["Twitter:ClientSecret"]
		},
		new CloudLoginConfiguration.Whataspp()
		{
			RequestUri = builder.Configuration["WhatsApp:RequestUri"],
			Authorization = builder.Configuration["WhatsApp:Authorization"],
			Template = "testcode",
			Language = "en"
		}
	}
});
builder.Services.AddAuthentication(opt =>
{
	opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});


builder.Services.AddOptions();
builder.Services.AddAuthenticationCore();

builder.Services.AddScoped<ServerAppTest.Controllers.CustomAuthenticationStateProvider>();

//builder.Services.AddScoped<IClaimsTransformation, UserInfoClaims>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
}
app.UseHsts();

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapControllers();
//app.MapRazorPages();
app.MapFallbackToPage("/_Host");

app.Run();
