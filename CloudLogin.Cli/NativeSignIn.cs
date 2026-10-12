using System.Net;
using AngryMonkey.CloudLogin.Native;

namespace AngryMonkey.CloudLogin.Cli;

public sealed record NativeSignInResult(CloudUser User, string Handoff, string Verifier, string State);

/// <summary>
/// The application's own HTTP stack: the backend's address and the session cookie, which nothing outside this client ever sees.
/// </summary>
public sealed class ApplicationClient : IDisposable
{
    private readonly HttpClient _http;

    public ApplicationClient(string applicationUrl, HttpMessageHandler transport, CookieContainer cookies, TimeSpan timeout)
    {
        Cookies = cookies;
        _http = new HttpClient(new CookieJarHandler(cookies) { InnerHandler = transport })
        {
            BaseAddress = new Uri(applicationUrl.TrimEnd('/') + "/"),
            Timeout = timeout
        };
        Native = new CloudLoginNativeClient(_http);
    }

    public CookieContainer Cookies { get; }
    public CloudLoginNativeClient Native { get; }
    public HttpClient Http => _http;

    public void Dispose() => _http.Dispose();
}

/// <summary>The native sign-in, step for step as the application performs it.</summary>
public sealed class NativeSignIn(string applicationUrl, string scheme)
{
    public string ApplicationUrl { get; } = applicationUrl;
    public string CallbackUrl { get; } = CloudLoginNativeProtocol.CallbackUrl(scheme);

    public async Task<NativeSignInResult> RunAsync(ISignInDriver driver, BrowserSession browser, ApplicationClient application, CancellationToken cancellationToken = default)
    {
        (string handoff, string verifier, string state) = await ReceiveHandoffAsync(driver, browser, cancellationToken);

        CloudUser? user = await application.Native.ExchangeAsync(handoff, verifier, cancellationToken);

        return user is null
            ? throw new CliSignInException("The backend refused to redeem the handoff for a session.")
            : new NativeSignInResult(user, handoff, verifier, state);
    }

    /// <summary>Everything up to the handoff, checked as the application checks it: the callback address, the state, and an error.</summary>
    public async Task<(string Handoff, string Verifier, string State)> ReceiveHandoffAsync(ISignInDriver driver, BrowserSession browser, CancellationToken cancellationToken = default)
    {
        string verifier = CloudLoginPkce.CreateVerifier();
        string state = CloudLoginPkce.CreateState();
        string loginUrl = CloudLoginNativeProtocol.LoginUrl(ApplicationUrl, verifier, state, CallbackUrl);
        string received = await driver.SignInAsync(browser, loginUrl, cancellationToken);

        if (!CloudLoginNativeProtocol.TryParseCallback(received, CallbackUrl, out CloudLoginNativeCallback callback))
            throw new CliSignInException($"The sign-in did not return to {CallbackUrl}.");

        if (!CloudLoginNativeProtocol.StateMatches(callback.State, state))
            throw new CliSignInException("The sign-in returned with a state this sign-in did not start, so it was not used.");

        if (callback.Error is { } error)
            throw new CliSignInException($"The backend ended the sign-in with '{error}'.");

        return callback.Handoff is { } handoff ? (handoff, verifier, state) : throw new CliSignInException("The sign-in returned without a handoff.");
    }
}
