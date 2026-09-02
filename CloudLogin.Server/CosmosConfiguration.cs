using AngryMonkey.CloudLogin.Server.Serialization;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace AngryMonkey.CloudLogin.Server;

public class CosmosConfiguration
{
    public CosmosConfiguration(IConfigurationSection configurationSection)
    {
        AspireName = configurationSection["AspireName"];
        ConnectionString = configurationSection["ConnectionString"];
        AccountEndpoint = configurationSection["AccountEndpoint"];
        // Local emulators (the Linux-based Cosmos emulator) support Gateway mode only.
        GatewayMode = configurationSection.GetValue("GatewayMode", false);
    }

    public CosmosConfiguration() { }

    public string? AspireName { get; set; }
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The Cosmos account endpoint, used with <see cref="Credential"/> to reach the account without
    /// an account key. Aspire supplies exactly this - and nothing else - for an account configured
    /// for managed identity, so a connection string is not always available to fall back on.
    /// </summary>
    public string? AccountEndpoint { get; set; }

    /// <summary>
    /// The credential authenticating against <see cref="AccountEndpoint"/>. Set in code rather than
    /// bound from configuration: a credential is an object, not a value, and the point of this mode
    /// is that nothing secret reaches configuration at all.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Connects in Cosmos Gateway mode instead of the SDK's default Direct mode. Required by
    /// local emulators (the Linux-based Cosmos emulator supports Gateway only); leave off
    /// (default) against real Azure Cosmos DB.
    /// </summary>
    public bool GatewayMode { get; set; }

    public bool IsValid() =>
        !string.IsNullOrEmpty(AspireName)
        || !string.IsNullOrEmpty(ConnectionString)
        || !string.IsNullOrEmpty(AccountEndpoint);

    /// <summary>
    /// Builds the Cosmos client for whichever authentication mode is configured. The one place the
    /// choice is made, so every caller gets both modes - and the custom serializer - without
    /// repeating either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An endpoint with a credential wins over a connection string: naming an endpoint and handing
    /// over a credential is a deliberate choice, while a connection string is often inherited from
    /// a configuration file nobody has revisited.
    /// </para>
    /// <para>
    /// <see cref="CosmosClientOptions.Serializer"/> is not optional here. This repository disables
    /// the Cosmos SDK's Newtonsoft.Json check on the grounds that every client uses
    /// <c>ConfigurableCosmosSerializer</c>; a client built without it reaches for an assembly that
    /// is not referenced and fails at runtime rather than at build.
    /// </para>
    /// </remarks>
    public CosmosClient CreateClient()
    {
        CosmosClientOptions options = new() { Serializer = new ConfigurableCosmosSerializer() };

        if (GatewayMode)
        {
            // Local emulators (the Linux-based Cosmos emulator) support Gateway mode only.
            options.ConnectionMode = ConnectionMode.Gateway;
            options.LimitToEndpoint = true;
        }

        if (Credential is not null && !string.IsNullOrWhiteSpace(AccountEndpoint))
            return new CosmosClient(AccountEndpoint, Credential, options);

        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return new CosmosClient(ConnectionString, options);

        throw new InvalidOperationException(
            "Cosmos is not configured for CloudLogin. Set Cosmos:AccountEndpoint together with a credential, " +
            "or Cosmos:ConnectionString.");
    }
}
