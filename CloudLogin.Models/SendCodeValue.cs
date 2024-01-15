namespace AngryMonkey.CloudLogin;

public class SendCodeValue(string code, string address)
{
    public string Code { get; set; } = code;
    public string Address { get; set; } = address;
}
