namespace AngryMonkey.CloudLogin;

/// <summary>
/// Scrubs a <see cref="CloudUser"/> before it crosses a trust boundary.
/// <para>
/// The model is shared between storage and transport, so it carries fields that
/// must never leave the server &mdash; most importantly <c>PasswordHash</c>. Every
/// path that returns a user to a caller runs it through here first, so the
/// redaction is one auditable place rather than a rule each endpoint remembers.
/// </para>
/// </summary>
public static class CloudLoginTransportSecurity
{
    /// <summary>
    /// Removes secret material from a user destined for an authenticated caller.
    /// </summary>
    public static CloudUser? ForTransport(CloudUser? user)
    {
        if (user is null)
            return null;

        return user with
        {
            Inputs = [.. user.Inputs.Select(input => input with
            {
                Providers = [.. input.Providers.Select(provider => provider with { PasswordHash = null })]
            })]
        };
    }

    /// <summary>
    /// Reduces a user to the bare minimum an <em>unauthenticated</em> caller may see
    /// during provider discovery: which login formats and providers exist, and
    /// nothing that identifies the person behind them.
    /// </summary>
    public static CloudUser? ForAnonymousDiscovery(CloudUser? user)
    {
        CloudUser? safe = ForTransport(user);

        if (safe is null)
            return null;

        return new CloudUser
        {
            Inputs = [.. safe.Inputs.Select(input => new CloudLoginInput
            {
                Format = input.Format,
                Providers = [.. input.Providers.Select(provider => new CloudLoginProvider
                {
                    Code = provider.Code
                })]
            })]
        };
    }
}
