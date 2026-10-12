using AngryMonkey.CloudLogin.Native;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// Holds the application's session with its backend. The application implements it because the session is the application's: its cookie
/// lives in the application's own HTTP stack, which carries it on every call to the backend. <see cref="MauiCloudLoginNativeClient"/>
/// performs the requests; the implementation supplies the client that holds the cookie and decides where the cookie is kept.
/// </summary>
public interface IMauiCloudLoginNativeExchange
{
    /// <summary>Returns the signed-in person, or null when the handoff was refused. The session cookie is kept by the implementation.</summary>
    Task<CloudUser?> ExchangeAsync(string handoff, string verifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session at the backend, which ends it at the authority for every application sharing it, and forgets it on this device
    /// whatever the outcome, so nothing keeps acting as the person. When the backend cannot be reached the implementation keeps only what it
    /// needs to finish ending it later (see <see cref="FinishPendingSessionEndAsync"/>), unused for anything else.
    /// </summary>
    Task<CloudLoginNativeSessionEndResults> EndSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CloudLoginNativeSessionEndResults.NoSession);

    /// <summary>Finishes ending a session that could not be ended at logout. Returns <see cref="CloudLoginNativeSessionEndResults.NoSession"/> when none is pending.</summary>
    Task<CloudLoginNativeSessionEndResults> FinishPendingSessionEndAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CloudLoginNativeSessionEndResults.NoSession);

    /// <summary>Forgets the session on this device without contacting the backend, for a session the backend already ended.</summary>
    Task ForgetSessionAsync() => Task.CompletedTask;
}

public static class MauiCloudLoginNativeClient
{
    /// <summary>
    /// Posts the handoff and the verifier to the application backend's <c>auth/native/exchange</c> through <paramref name="client"/>, whose cookie
    /// container receives the session. Only the application that started the sign-in holds the verifier, so only it can redeem the handoff.
    /// </summary>
    public static Task<CloudUser?> ExchangeAsync(HttpClient client, string handoff, string verifier, CancellationToken cancellationToken = default) =>
        new CloudLoginNativeClient(client).ExchangeAsync(handoff, verifier, cancellationToken);

    /// <summary>Posts to the backend's <c>auth/native/logout</c> with the session cookie <paramref name="client"/> holds.</summary>
    public static Task<CloudLoginNativeSessionEndResults> EndSessionAsync(HttpClient client, CancellationToken cancellationToken = default) =>
        new CloudLoginNativeClient(client).EndSessionAsync(cancellationToken);
}
