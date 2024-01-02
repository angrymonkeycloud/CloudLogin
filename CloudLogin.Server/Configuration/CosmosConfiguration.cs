using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin;

public class CosmosConfiguration(IConfigurationSection configurationSection)
{
    public string? ConnectionString { get; set; } = configurationSection["ConnectionString"];
    public string? DatabaseId { get; set; } = configurationSection["DatabaseId"];
    public string? ContainerId { get; set; } = configurationSection["ContainerId"];
}
