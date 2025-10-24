namespace AngryMonkey.CloudLogin.Server;

using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

public class AzureStorageConfiguration
{
    public AzureStorageConfiguration() { }

    public AzureStorageConfiguration(IConfigurationSection configurationSection)
    {
        ConnectionString = configurationSection.GetValue<string>("ConnectionString") ?? throw new ArgumentNullException("ConnectionString");
    }

    public string ConnectionString { get; init; }

    public string ContainerName { get; set; } = "users";

    private string? _publicBaseUrl;
    public string? PublicBaseUrl
    {
        get => _publicBaseUrl ?? TryBuildPublicBaseUrl();
        set => _publicBaseUrl = value;
    }

    private string? TryBuildPublicBaseUrl()
    {
        Match match = Regex.Match(ConnectionString, @"AccountName=([^;]+)", RegexOptions.IgnoreCase);

        return !match.Success ? null : $"https://{match.Groups[1].Value}.blob.core.windows.net/{ContainerName.Trim('/')}/";
    }
}
