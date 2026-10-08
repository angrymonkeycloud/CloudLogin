using System.Net.Http.Json;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// Redeems the handoff a native sign-in ends with for the application's own session. The application implements it because the session is
/// the application's: its cookie lives in the application's own HTTP stack, which then carries it on every call to its backend.
/// <see cref="MauiCloudLoginNativeClient"/> performs the request; the implementation only supplies the client that holds the cookie.
/// </summary>
public interface IMauiCloudLoginNativeExchange
{
    /// <summary>Returns the signed-in person, or null when the handoff was refused. The session cookie is kept by the implementation.</summary>
    Task<CloudUser?> ExchangeAsync(string handoff, string verifier, CancellationToken cancellationToken = default);
}

public static class MauiCloudLoginNativeClient
{
    /// <summary>
    /// Posts the handoff and the verifier to the application backend's <c>auth/native/exchange</c> through <paramref name="client"/>, whose cookie
    /// container receives the session. Only the application that started the sign-in holds the verifier, so only it can redeem the handoff.
    /// </summary>
    public static async Task<CloudUser?> ExchangeAsync(HttpClient client, string handoff, string verifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "auth/native/exchange",
            new { Handoff = handoff, Verifier = verifier },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<CloudUser>(CloudLoginSerialization.Options, cancellationToken);
    }
}
