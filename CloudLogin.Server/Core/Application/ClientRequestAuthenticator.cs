using System.Text;
using Microsoft.AspNetCore.Http;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

public enum ClientCredentialMethods
{
    None,
    SecretKey,
    Public
}

public sealed record ClientRequestAuthentication(ClientAuthenticationResult Result, ClientCredentialMethods Method)
{
    public bool Succeeded => Result.Succeeded;

    public ClientIdentity? Client => Result.Client;
}

public interface IClientRequestAuthenticator
{
    /// <summary>
    /// A backend authenticates with <c>Authorization: Basic base64(name:secretKey)</c>. Without it, <paramref name="publicClientId"/> (a
    /// website's or app's origin) identifies a public client.
    /// </summary>
    Task<ClientRequestAuthentication> AuthenticateAsync(HttpRequest request, string? publicClientId = null, CancellationToken cancellationToken = default);
}

public sealed class ClientRequestAuthenticator(IClientDirectory directory) : IClientRequestAuthenticator
{
    public async Task<ClientRequestAuthentication> AuthenticateAsync(HttpRequest request, string? publicClientId = null, CancellationToken cancellationToken = default)
    {
        string header = request.Headers.Authorization.ToString();

        if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryReadBasic(header, out string clientId, out string secret))
                return new(new(null, ClientAuthenticationFailures.InvalidClientId), ClientCredentialMethods.SecretKey);

            return new(await directory.AuthenticateAsync(clientId, secret, cancellationToken), ClientCredentialMethods.SecretKey);
        }

        if (!string.IsNullOrWhiteSpace(publicClientId))
            return new(await directory.AuthenticatePublicAsync(publicClientId, cancellationToken), ClientCredentialMethods.Public);

        return new(new(null, ClientAuthenticationFailures.InvalidClientId), ClientCredentialMethods.None);
    }

    private static bool TryReadBasic(string header, out string clientId, out string secret)
    {
        clientId = string.Empty;
        secret = string.Empty;

        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            int separator = decoded.IndexOf(':');

            if (separator <= 0)
                return false;

            clientId = Uri.UnescapeDataString(decoded[..separator]);
            secret = Uri.UnescapeDataString(decoded[(separator + 1)..]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
