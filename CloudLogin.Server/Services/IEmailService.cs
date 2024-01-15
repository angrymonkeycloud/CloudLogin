namespace AngryMonkey.CloudLogin.Services;

public interface IEmailService
{
    Task SendEmail(string subject, string body, List<string> ToEmails);
}
