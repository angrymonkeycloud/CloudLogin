using AngryMonkey.Cloud.Login.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddCloudLogin(new CloudLoginConfiguration()
{
	Cosmos = new CloudLoginConfiguration.CosmosDatabase()
	{
		ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
		DatabaseId = "CloudLogin",
		ContainerId = "Accounts"
	},

	Providers = new List<CloudLoginConfiguration.Provider>()
	{
		new CloudLoginConfiguration.MicrosoftAccount()
		{
			ClientId = "642c7f39-ba37-453e-8834-420a550d2fc1",
			ClientSecret= "cWs8Q~B7FgbbTCRGNmc4dvkLbKcF5CuRoDssaapT"
		}
	}
});

var app = builder.Build();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapControllers();
//app.MapRazorPages();
app.MapFallbackToPage("/_Host");

app.Run();
