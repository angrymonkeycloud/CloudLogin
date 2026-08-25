namespace AngryMonkey.CloudLogin.Server;

using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

/// <summary>
/// How CloudLogin reaches the blob container holding profile pictures and per-user security
/// documents.
/// </summary>
/// <remarks>
/// Two ways to authenticate, and exactly one of them must be configured:
/// <list type="bullet">
/// <item><see cref="AccountName"/> with a <see cref="Credential"/> - Microsoft Entra credentials,
/// the preferred path. No key is held by the application or written to its configuration.</item>
/// <item><see cref="ConnectionString"/> - the account key, still fully supported. Local emulators
/// (Azurite) and deployments that have not moved to credentials work exactly as they always
/// have.</item>
/// </list>
/// Both are optional so that either can be the one supplied; <see cref="IsValid"/> reports whether
/// one of them was.
/// </remarks>
public class AzureStorageConfiguration
{
    public AzureStorageConfiguration() { }

    /// <summary>
    /// Binds from a configuration section. Neither authentication mode is required here - a host
    /// that supplies the account name in configuration and the credential in code (the usual shape
    /// when running on a managed identity) is configuring this object over two steps, and throwing
    /// on the first would make that impossible.
    /// </summary>
    public AzureStorageConfiguration(IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(configurationSection);

        ConnectionString = configurationSection["ConnectionString"];
        AccountName = configurationSection["AccountName"];

        if (Uri.TryCreate(configurationSection["BlobEndpoint"], UriKind.Absolute, out Uri? blobEndpoint))
            BlobEndpoint = blobEndpoint;

        if (configurationSection["ContainerName"] is { Length: > 0 } containerName)
            ContainerName = containerName;

        if (configurationSection["PublicBaseUrl"] is { Length: > 0 } publicBaseUrl)
            PublicBaseUrl = publicBaseUrl;
    }

    /// <summary>The account key connection string, or <see langword="null"/> when using credentials.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// The storage account name, used with <see cref="Credential"/> to reach the account without a
    /// key. The blob endpoint is derived from it.
    /// </summary>
    public string? AccountName { get; init; }

    private Uri? _blobEndpoint;

    /// <summary>
    /// The explicit blob service endpoint used with <see cref="Credential"/>. When omitted, it is
    /// derived from <see cref="AccountName"/> for compatibility with existing configuration.
    /// </summary>
    public Uri? BlobEndpoint
    {
        get => _blobEndpoint ?? (string.IsNullOrWhiteSpace(AccountName)
            ? null
            : new Uri($"https://{AccountName}.blob.core.windows.net"));
        init => _blobEndpoint = value;
    }

    /// <summary>
    /// The credential authenticating against <see cref="AccountName"/>. Set in code rather than
    /// bound from configuration - a credential is an object, not a value, and the whole point of
    /// this mode is that nothing secret appears in configuration.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    public string ContainerName { get; set; } = "users";

    private string? _publicBaseUrl;
    public string? PublicBaseUrl { get => _publicBaseUrl ?? TryBuildPublicBaseUrl(); set => _publicBaseUrl = value; }

    /// <summary>Whether either authentication mode has been configured.</summary>
    public bool IsValid() => !string.IsNullOrWhiteSpace(ConnectionString) || BlobEndpoint is not null;


    /// <summary>
    /// Builds a client for the configured container. The single place either authentication mode is
    /// turned into a client, so every caller gets both without repeating the choice.
    /// </summary>
    /// <remarks>
    /// A credential wins when both are configured: naming an account and supplying a credential is
    /// a deliberate choice, while a connection string is often inherited from a file nobody has
    /// revisited.
    /// </remarks>
    public BlobContainerClient CreateContainerClient()
    {
        if (Credential is not null && BlobEndpoint is { } endpoint)
            return new BlobServiceClient(endpoint, Credential).GetBlobContainerClient(ContainerName);

        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return new BlobContainerClient(ConnectionString, ContainerName);

        throw new InvalidOperationException(
            "Azure Storage is not configured for CloudLogin. Set Storage:BlobEndpoint together with a " +
            "credential, or Storage:ConnectionString.");
    }

    /// <summary>
    /// The public URL prefix for blobs in the container, derived from whichever mode is configured.
    /// </summary>
    private string? TryBuildPublicBaseUrl()
    {
        if (BlobEndpoint is { } endpoint)
            return $"{endpoint.AbsoluteUri.TrimEnd('/')}/{ContainerName.Trim('/')}/";

        if (string.IsNullOrWhiteSpace(ConnectionString))
            return null;

        Match match = Regex.Match(ConnectionString, @"AccountName=([^;]+)", RegexOptions.IgnoreCase);

        return !match.Success ? null : $"https://{match.Groups[1].Value}.blob.core.windows.net/{ContainerName.Trim('/')}/";
    }
}
