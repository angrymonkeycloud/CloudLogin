using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AngryMonkey.CloudLogin.Server.Core.Application;

/// <summary>
/// Refuses to start on an identity key that did not write this realm's identity index.
/// </summary>
/// <remarks>
/// <para>
/// Startup is the only place this can be caught cheaply and the only place failing is still safe.
/// Serving on the wrong key does not fail — it quietly forks people's accounts, one sign-in at a
/// time, and the damage is already done by the time anyone notices two of someone.
/// </para>
/// <para>
/// Only a proven mismatch stops the host. Anything that merely prevents the check from reaching an
/// answer - the table unreachable, a transient failure - is logged and allowed through, because a
/// login service that will not start is its own outage and an unverifiable key is not evidence of a
/// wrong one.
/// </para>
/// </remarks>
internal sealed class CloudLoginIdentityKeyGuard(
    IdentityKeyVerification verification,
    ILogger<CloudLoginIdentityKeyGuard> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await verification.VerifyOrThrowAsync(cancellationToken);
        }
        catch (IdentityHmacSecretException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "CloudLogin could not verify its identity key against the identity index. Continuing: an " +
                "unreachable index is not evidence of a wrong key.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
