namespace Microsoft.Extensions.DependencyInjection;

public class CloudLoginServerConfiguration
{
    public string CookieName { get; set; } = "CloudLogin.Consumer";
    public TimeSpan SessionDuration { get; set; } = TimeSpan.FromDays(30);
}
