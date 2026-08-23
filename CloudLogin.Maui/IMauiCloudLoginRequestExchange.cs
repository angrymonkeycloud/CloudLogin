namespace AngryMonkey.CloudLogin;

/// <summary>
/// Allows a native client API to consume the one-time CloudLogin request and establish
/// its own authenticated session while returning the resolved user in the same exchange.
/// </summary>
public interface IMauiCloudLoginRequestExchange
{
    Task<CloudUser?> ExchangeAsync(
        string requestId,
        CancellationToken cancellationToken = default);
}
