using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Options;

namespace AngryMonkey.CloudLogin.Services;

public class EmailService : IEmailService
{
    private readonly string[] _scopes = ["https://graph.microsoft.com/.default"];
    private readonly EmailServiceOptions _options;
    private readonly GraphServiceClient _graphServiceClient;

    public EmailService(IOptions<EmailServiceOptions> options)
    {
        _options = options.Value;

        ClientSecretCredential clientSecretCredential = new(_options.TenantId, _options.ClientId, _options.Secret);

        _graphServiceClient = new GraphServiceClient(clientSecretCredential, _scopes);
    }

    public async Task SendEmail(string subject, string body, List<string> ToEmails)
    {
        Message message = new()
        {
            Subject = subject,
            Body = new ItemBody { ContentType = BodyType.Html, Content = body },
            ToRecipients =
            [
                new()
                {
                    EmailAddress = new() { Address = string.Join(";", ToEmails) }
                }
            ],
            BccRecipients =
            [
                new()
                {
                    EmailAddress = new() { Address = _options.BccEmail }
                }
            ],
            From = new() { EmailAddress = new() { Address = _options.FromEmail } },
        };

        await _graphServiceClient.Users["elietebchrani@coverbox.app"].SendMail.PostAsync(new()
        {
            Message = message,
            SaveToSentItems = true
        });
    }
}
