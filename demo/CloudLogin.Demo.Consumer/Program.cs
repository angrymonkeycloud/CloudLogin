using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddRateLimiter();

// Matches the fixed port used by demo/CloudLogin.Demo (the standalone authority).
builder.Services.AddCloudLoginServer("https://localhost:7100");
builder.Services.AddCloudLoginTokenAuthentication(options =>
{
    options.Authority = "https://localhost:7100";
    options.Audience = "cloudlogin-demo-consumer";
    options.ClientId = "cloudlogin-demo-consumer";
    options.ClientSecret = "local-demo-only-client-secret-32-chars";
});

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCloudLoginSecurity();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();

app.Run();
