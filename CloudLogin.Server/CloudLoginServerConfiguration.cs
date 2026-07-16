namespace Microsoft.Extensions.DependencyInjection;

public class CloudLoginServerConfiguration
{
    public string? LoginUrl { get; set; }
    public string CookieName { get; set; } = "__Host-CloudLogin.Consumer";
    public TimeSpan SessionDuration { get; set; } = TimeSpan.FromHours(8);
    public bool RequireHttps { get; set; } = true;
}
