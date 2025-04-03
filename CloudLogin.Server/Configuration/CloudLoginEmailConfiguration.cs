using AngryMonkey.CloudLogin.Services;

namespace AngryMonkey.CloudLogin.Server;

public class CloudLoginEmailConfiguration
{
    public required IEmailService EmailService { get; set; }

    public string DefaultSubject = "Email Code Verification";
    public string DefaultBody = $"""
        <div style="width:300px;margin:20px auto;padding: 15px;border:1px dashed  #4569D4;text-align:center">
        <h3>Verification Code:</p>
        <div style="width:150px;border:1px solid #4569D4;margin: 0 auto;padding: 10px;text-align:center;">
        <b style="color:#202124;text-decoration:none">{VerificationCodePlaceHolder}</b> <br />
        </div></div>
        """;
    public static readonly string VerificationCodePlaceHolder = "{verification-code}";
}