namespace AngryMonkey.CloudLogin;

public class CloudLoginSendCodeValue(string code, string address)
{
    public string Code { get; set; } = code;
    public string Address { get; set; } = address;
}
