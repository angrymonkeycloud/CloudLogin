using System.Diagnostics;
using System.Net;
using AngryMonkey.CloudLogin.Native;

namespace AngryMonkey.CloudLogin.Cli;

public enum ScenarioStepStatuses
{
    Passed,
    Failed,
    Skipped
}

public sealed record ScenarioStepResult(int Number, string Name, ScenarioStepStatuses Status, string Detail, TimeSpan Duration);

/// <summary>
/// Runs a native application's whole session life against a live deployment and checks each step: what a person relies on (sign-in,
/// renewal, logout) and what an attacker would try (a reused handoff, a stolen one, a web page ending the session).
/// </summary>
public sealed class ScenarioRunner(CliEnvironment environment, string applicationUrl, string scheme, Func<ISignInDriver> createDriver, int parallelRequests)
{
    private sealed class StepFailedException(string message) : Exception(message);

    private readonly NativeSignIn _signIn = new(applicationUrl, scheme);
    private readonly CookieContainer _browserCookies = new();
    private CookieContainer _applicationCookies = new();
    private IReadOnlyList<CliCookie> _endedSession = [];
    private bool _signedIn;
    private bool _ended;

    public static IReadOnlyList<string> StepNames { get; } =
    [
        "The backend refuses a request that carries no session",
        "Signing in returns to the application with the state it started",
        "The handoff is redeemed for a session",
        "The same handoff cannot be redeemed a second time",
        "The session is accepted",
        "A handoff is useless without its verifier, and the attempt burns it",
        "Requests that renew the session at the same moment all succeed, and the session lives",
        "A request carrying an older copy of the cookie is still accepted",
        "A web page cannot end the session",
        "Logout ends the session",
        "The ended session's cookie is refused",
        "Ending it again reports that nothing is left",
        "The sign-in at the authority ended too, so the next sign-in asks again",
        "Signing in again after logout works"
    ];

    public async Task<IReadOnlyList<ScenarioStepResult>> RunAsync(Action<ScenarioStepResult>? onStep = null, CancellationToken cancellationToken = default)
    {
        List<ScenarioStepResult> results = [];

        Func<CancellationToken, Task<string>>[] steps =
        [
            RefusesWithoutSessionAsync,
            SignInAsync,
            RedeemAsync,
            CannotRedeemTwiceAsync,
            SessionAcceptedAsync,
            VerifierRequiredAsync,
            ParallelRenewalAsync,
            OlderCookieAsync,
            WebPageCannotEndAsync,
            LogoutAsync,
            EndedCookieRefusedAsync,
            EndAgainAsync,
            AuthorityEndedAsync,
            SignInAgainAsync
        ];

        for (int index = 0; index < steps.Length; index++)
        {
            ScenarioStepResult result = await RunStepAsync(index + 1, steps[index], Prerequisite(index + 1), cancellationToken);
            results.Add(result);
            onStep?.Invoke(result);
        }

        return results;
    }

    private string? Prerequisite(int step) => step switch
    {
        3 when _pendingHandoff is null => "needs the handoff from step 2",
        4 or 5 or 7 or 8 or 9 or 10 when !_signedIn => "needs the session from step 3",
        11 or 12 or 13 when !_ended => "needs the session ended in step 10",
        _ => null
    };

    private async Task<ScenarioStepResult> RunStepAsync(int number, Func<CancellationToken, Task<string>> step, string? skipReason, CancellationToken cancellationToken)
    {
        string name = StepNames[number - 1];

        if (skipReason is not null)
            return new ScenarioStepResult(number, name, ScenarioStepStatuses.Skipped, skipReason, TimeSpan.Zero);

        Stopwatch watch = Stopwatch.StartNew();

        try
        {
            string detail = await step(cancellationToken);
            return new ScenarioStepResult(number, name, ScenarioStepStatuses.Passed, detail, watch.Elapsed);
        }
        catch (Exception exception) when (exception is StepFailedException or CliSignInException or HttpRequestException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            string detail = exception is TaskCanceledException ? "timed out" : exception.Message;
            return new ScenarioStepResult(number, name, ScenarioStepStatuses.Failed, detail, watch.Elapsed);
        }
    }

    private (string Handoff, string Verifier)? _pendingHandoff;

    private ApplicationClient Application(CookieContainer cookies) => new(applicationUrl, environment.CreateTransport(), cookies, environment.RequestTimeout);

    private BrowserSession Browser() => new(environment.CreateTransport(), _browserCookies, environment.RequestTimeout);

    private static void Expect(bool condition, string failure)
    {
        if (!condition)
            throw new StepFailedException(failure);
    }

    private async Task<string> RefusesWithoutSessionAsync(CancellationToken cancellationToken)
    {
        using ApplicationClient anonymous = Application(new CookieContainer());
        CloudLoginNativeTokenResult token = await anonymous.Native.GetTokenAsync(cancellationToken: cancellationToken);

        Expect(token.Status != CloudLoginNativeTokenStatuses.Issued, "the backend issued a token to a request with no session");
        Expect(token.Status == CloudLoginNativeTokenStatuses.SessionOver, "the backend did not answer");
        return "refused";
    }

    private async Task<string> SignInAsync(CancellationToken cancellationToken)
    {
        using BrowserSession browser = Browser();
        (string handoff, string verifier, _) = await _signIn.ReceiveHandoffAsync(createDriver(), browser, cancellationToken);

        _pendingHandoff = (handoff, verifier);
        return "handoff received, state matched";
    }

    private async Task<string> RedeemAsync(CancellationToken cancellationToken)
    {
        (string handoff, string verifier) = _pendingHandoff!.Value;
        using ApplicationClient application = Application(_applicationCookies);
        CloudUser? user = await application.Native.ExchangeAsync(handoff, verifier, cancellationToken);

        Expect(user is not null, "the backend refused the handoff");
        Expect(_applicationCookies.Count > 0, "the backend returned no session cookie");

        _signedIn = true;
        return $"signed in as {user!.DisplayName ?? user.Id.ToString()}";
    }

    private async Task<string> CannotRedeemTwiceAsync(CancellationToken cancellationToken)
    {
        (string handoff, string verifier) = _pendingHandoff!.Value;
        using ApplicationClient other = Application(new CookieContainer());

        Expect(await other.Native.ExchangeAsync(handoff, verifier, cancellationToken) is null, "the handoff was redeemed a second time");
        return "refused";
    }

    private async Task<string> SessionAcceptedAsync(CancellationToken cancellationToken)
    {
        using ApplicationClient application = Application(_applicationCookies);
        CloudLoginNativeTokenResult token = await application.Native.GetTokenAsync(cancellationToken: cancellationToken);

        Expect(token.Status == CloudLoginNativeTokenStatuses.Issued, $"the session was not accepted ({token.Status})");
        return $"token issued for {token.Audience}, lasting {token.ExpiresIn} s";
    }

    private async Task<string> VerifierRequiredAsync(CancellationToken cancellationToken)
    {
        using BrowserSession browser = new(environment.CreateTransport(), new CookieContainer(), environment.RequestTimeout);
        (string handoff, string verifier, _) = await _signIn.ReceiveHandoffAsync(createDriver(), browser, cancellationToken);
        using ApplicationClient thief = Application(new CookieContainer());
        using ApplicationClient owner = Application(new CookieContainer());

        Expect(await thief.Native.ExchangeAsync(handoff, CloudLoginPkce.CreateVerifier(), cancellationToken) is null, "a handoff was redeemed with the wrong verifier");
        Expect(await owner.Native.ExchangeAsync(handoff, verifier, cancellationToken) is null, "a handoff stayed usable after a wrong verifier was tried");
        return "wrong verifier refused, handoff burned";
    }

    private async Task<string> ParallelRenewalAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CliCookie> snapshot = CliSession.Capture(_applicationCookies);
        CookieContainer[] copies = [.. Enumerable.Range(0, parallelRequests).Select(_ => CliSession.Restore(snapshot))];
        ApplicationClient[] clients = [.. copies.Select(Application)];

        try
        {
            CloudLoginNativeTokenResult[] results = await Task.WhenAll(clients.Select(client => client.Native.GetTokenAsync(cancellationToken: cancellationToken)));
            int refused = results.Count(result => result.Status != CloudLoginNativeTokenStatuses.Issued);

            Expect(refused == 0, $"{refused} of {parallelRequests} requests were refused");

            _applicationCookies = copies[0];
            using ApplicationClient after = Application(_applicationCookies);
            Expect((await after.Native.GetTokenAsync(cancellationToken: cancellationToken)).Status == CloudLoginNativeTokenStatuses.Issued, "the session did not survive the renewals");

            return $"{parallelRequests} of {parallelRequests} accepted";
        }
        finally
        {
            foreach (ApplicationClient client in clients)
                client.Dispose();
        }
    }

    private async Task<string> OlderCookieAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CliCookie> older = CliSession.Capture(_applicationCookies);

        using (ApplicationClient current = Application(_applicationCookies))
            Expect((await current.Native.GetTokenAsync(cancellationToken: cancellationToken)).Status == CloudLoginNativeTokenStatuses.Issued, "the session was not accepted");

        bool rotated = !older.Select(cookie => cookie.Value).SequenceEqual(CliSession.Capture(_applicationCookies).Select(cookie => cookie.Value));

        using ApplicationClient stale = Application(CliSession.Restore(older));
        Expect((await stale.Native.GetTokenAsync(cancellationToken: cancellationToken)).Status == CloudLoginNativeTokenStatuses.Issued, "the older cookie was refused");

        using ApplicationClient latest = Application(_applicationCookies);
        Expect((await latest.Native.GetTokenAsync(cancellationToken: cancellationToken)).Status == CloudLoginNativeTokenStatuses.Issued, "the session ended after the older cookie was used");

        return rotated ? "accepted after the cookie was renewed" : "accepted (the cookie was not renewed in between)";
    }

    private async Task<string> WebPageCannotEndAsync(CancellationToken cancellationToken)
    {
        using ApplicationClient application = Application(_applicationCookies);
        using HttpRequestMessage forged = new(HttpMethod.Post, CloudLoginNativeProtocol.SessionEndPath);
        forged.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        forged.Headers.TryAddWithoutValidation("Origin", "https://attacker.example");

        using HttpResponseMessage response = await application.Http.SendAsync(forged, cancellationToken);

        Expect(response.StatusCode == HttpStatusCode.Forbidden, $"a cross-site request was answered {(int)response.StatusCode}");
        Expect((await application.Native.GetTokenAsync(cancellationToken: cancellationToken)).Status == CloudLoginNativeTokenStatuses.Issued, "the session did not survive the attempt");
        return "refused, session intact";
    }

    private async Task<string> LogoutAsync(CancellationToken cancellationToken)
    {
        _endedSession = CliSession.Capture(_applicationCookies);
        using ApplicationClient application = Application(_applicationCookies);
        CloudLoginNativeSessionEndResults result = await application.Native.EndSessionAsync(cancellationToken);

        Expect(result == CloudLoginNativeSessionEndResults.Ended, $"logout answered {result}");
        _ended = true;
        return "ended";
    }

    private async Task<string> EndedCookieRefusedAsync(CancellationToken cancellationToken)
    {
        using ApplicationClient ended = Application(CliSession.Restore(_endedSession));
        CloudLoginNativeTokenResult token = await ended.Native.GetTokenAsync(cancellationToken: cancellationToken);

        Expect(token.Status == CloudLoginNativeTokenStatuses.SessionOver, token.Status == CloudLoginNativeTokenStatuses.Issued ? "the ended session was still accepted" : "the backend did not answer");
        return "refused";
    }

    private async Task<string> EndAgainAsync(CancellationToken cancellationToken)
    {
        using ApplicationClient ended = Application(CliSession.Restore(_endedSession));
        CloudLoginNativeSessionEndResults result = await ended.Native.EndSessionAsync(cancellationToken);

        Expect(result == CloudLoginNativeSessionEndResults.NoSession, $"answered {result}");
        return "nothing left to end";
    }

    private async Task<string> AuthorityEndedAsync(CancellationToken cancellationToken)
    {
        using BrowserSession browser = Browser();
        AuthorityPage page = await AuthorityPage.OpenAsync(browser, CloudLoginNativeProtocol.LoginUrl(applicationUrl, CloudLoginPkce.CreateVerifier(), CloudLoginPkce.CreateState(), _signIn.CallbackUrl), cancellationToken);

        try
        {
            await page.CompleteAsync(browser, cancellationToken);
        }
        catch (CliSignInException)
        {
            return "the authority asks who is signing in";
        }

        throw new StepFailedException("the authority still had the person signed in, so the next sign-in would not ask");
    }

    private async Task<string> SignInAgainAsync(CancellationToken cancellationToken)
    {
        CookieContainer cookies = new();
        using BrowserSession browser = Browser();
        using ApplicationClient application = Application(cookies);
        NativeSignInResult signedIn = await _signIn.RunAsync(createDriver(), browser, application, cancellationToken);

        Expect((await application.Native.GetTokenAsync(cancellationToken: cancellationToken)).Status == CloudLoginNativeTokenStatuses.Issued, "the new session was not accepted");
        Expect(await application.Native.EndSessionAsync(cancellationToken) == CloudLoginNativeSessionEndResults.Ended, "the new session could not be ended afterwards");

        return $"signed in as {signedIn.User.DisplayName ?? signedIn.User.Id.ToString()} and out again";
    }
}
