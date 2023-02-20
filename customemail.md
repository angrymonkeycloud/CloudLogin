# Cloud Login Custom Email Integration Guide

## Main configuration, inside the CloudLoginConfiguration

To add a custom email login, a SMTP Email is required to be used for sending Email Codes for verification.<br/>
To add the SMTP Email, you should add the following code:
```csharp
EmailSendCodeRequest = async (sendCode) =>
{
    <Code for configuration here>
}
```
For adding SMTP Email you should:
1.Create an SMTP Client:
```csharp
SmtpClient smtpClient = new(builder.Configuration["SMTP:Host"], int.Parse(builder.Configuration["SMTP:Port"]))
    {
        EnableSsl = true,
        DeliveryMethod = SmtpDeliveryMethod.Network,
        UseDefaultCredentials = false,
        Credentials = new NetworkCredential(builder.Configuration["SMTP:Email"], builder.Configuration["SMTP:Password"])
    };
```
2. Create String Builder for the mail view:
```csharp
StringBuilder mailBody = new();
mailBody.AppendLine($"code: <b style=\"color:#202124;text-decoration:none\">{sendCode.Code}</b> <br />");
```
3. Configure the Email:
```csharp
MailMessage mailMessage = new()
{
    From = new MailAddress(builder.Configuration["SMTP:Email"], "Cloud Login"),
    Subject = "Login Code",
    IsBodyHtml = true,
    Body = mailBody.ToString()
};
mailMessage.To.Add(sendCode.Address);
```
4. Sending the email:
```csharp
await smtpClient.SendMailAsync(mailMessage);
```
All these should be added inside the EmailSendCodeRequest mentioned above.
## Configuration inside the app secret:

For each part added above you should add the configuration of it inside the secret, For example:
```csharp
    "SMTP": {
        "Email": "<Email that's configured for SMTP>",
        "Password": "<SMTP Email Password>",
        "Host": "<SMTP Email Host>",
        "Port": "<SMTP Email Port>"
    }
```

For adding an SMTP Google Email, please see the [Gmail SMTP Settings](https://www.gmass.co/blog/gmail-smtp/).