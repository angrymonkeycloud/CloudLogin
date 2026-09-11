using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Aspire;
using AngryMonkey.CloudLogin.Server;
using CoconutSharp.Communications;
using CoconutSharp.Communications.Email;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

ServiceProvider communications = new ServiceCollection()
    .AddCoconutCommunications(builder.Configuration)
    .BuildServiceProvider();

CloudLoginWebConfiguration configuration = builder.ReadCloudLoginConfiguration();

if (communications.GetServices<IEmailProvider>().Any())
    configuration.EmailSendCodeRequest = value => SendVerificationCodeAsync(communications, configuration, value);

builder.AddCloudLoginWeb(configuration);

await CloudLoginWeb.InitApp(builder);

static async Task SendVerificationCodeAsync(ServiceProvider communications, CloudLoginWebConfiguration configuration, CloudLoginSendCodeValue value)
{
    string title = string.IsNullOrWhiteSpace(configuration.Title) ? "CloudLogin" : configuration.Title;
    string color = configuration.PrimaryColor;

    CommunicationResult result = await communications.GetRequiredService<IEmailSender>().SendAsync(
        value.Address,
        $"Your {title} verification code",
        $"""
        <div style="width:300px;margin:20px auto;padding:15px;border:1px dashed {color};text-align:center;font-family:sans-serif">
        <h3 style="margin:0 0 12px">{title} verification code</h3>
        <div style="border:1px solid {color};padding:10px;font-size:20px;letter-spacing:4px;color:{color}">{value.Code}</div>
        </div>
        """);

    if (!result.Succeeded)
        throw new InvalidOperationException($"Could not send the {title} verification code: {result.Error}");
}
