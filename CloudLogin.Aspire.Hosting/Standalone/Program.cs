using AngryMonkey.CloudLogin.Aspire;
using AngryMonkey.CloudLogin.Server;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
CloudLoginWebConfiguration configuration = builder.ReadCloudLoginConfiguration();
builder.AddCloudLoginWeb(configuration);
await CloudLoginWeb.InitApp(builder);
