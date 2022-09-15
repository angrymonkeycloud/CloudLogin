using AngryMonkey.Cloud.Login.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using ServerAppTest.Controllers;

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
        new CloudLoginConfiguration.EmailAccount() { }
    }
});
builder.Services.AddAuthentication(opt =>
{
    opt.DefaultScheme  = CookieAuthenticationDefaults.AuthenticationScheme;
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
