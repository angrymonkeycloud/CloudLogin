using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AngryMonkey.CloudLogin.Native;

namespace AngryMonkey.CloudLogin.Cli;

/// <summary>The <c>cloudlogin</c> command line: signs in as a native application does, and checks a deployment's sessions end to end.</summary>
public static class CloudLoginCli
{
    public const string ApplicationVariable = "CLOUDLOGIN_APP";
    public const string SchemeVariable = "CLOUDLOGIN_SCHEME";
    public const string ModeVariable = "CLOUDLOGIN_MODE";
    public const string EmailVariable = "CLOUDLOGIN_EMAIL";
    public const string PasswordVariable = "CLOUDLOGIN_PASSWORD";
    public const string SessionVariable = "CLOUDLOGIN_SESSION";

    private static readonly JsonSerializerOptions JsonOutput = new() { WriteIndented = true };

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CliEnvironment environment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);

        CliArguments arguments;

        try
        {
            arguments = CliArguments.Parse(args ?? []);
        }
        catch (CliUsageException exception)
        {
            await environment.Error.WriteLineAsync(exception.Message);
            return CliExitCodes.Usage;
        }

        if (arguments.Flag("help"))
        {
            await environment.Out.WriteLineAsync(CliHelp.For(arguments.Command));
            return CliExitCodes.Success;
        }

        try
        {
            return arguments.Command switch
            {
                "help" => await HelpAsync(arguments, environment),
                "version" => await VersionAsync(environment),
                "login" => await LoginAsync(arguments, environment, cancellationToken),
                "status" => await StatusAsync(arguments, environment, cancellationToken),
                "token" => await TokenAsync(arguments, environment, cancellationToken),
                "call" => await CallAsync(arguments, environment, cancellationToken),
                "logout" => await LogoutAsync(arguments, environment, cancellationToken),
                "scenario" => await ScenarioAsync(arguments, environment, cancellationToken),
                _ => throw new CliUsageException($"Unknown command '{arguments.Command}'.")
            };
        }
        catch (CliUsageException exception)
        {
            await environment.Error.WriteLineAsync(exception.Message);
            return CliExitCodes.Usage;
        }
        catch (CliSignInException exception)
        {
            await environment.Error.WriteLineAsync($"Sign-in failed: {exception.Message}");
            return CliExitCodes.Failed;
        }
        catch (HttpRequestException exception)
        {
            await environment.Error.WriteLineAsync($"Could not reach the server: {exception.Message}");
            return CliExitCodes.Unavailable;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync("The server did not answer in time.");
            return CliExitCodes.Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync("Cancelled.");
            return CliExitCodes.Failed;
        }
    }

    private static async Task<int> HelpAsync(CliArguments arguments, CliEnvironment environment)
    {
        string? topic = arguments.Positional.Count > 0 ? arguments.Positional[0].ToLowerInvariant() : null;

        if (topic is not null && !CliArguments.Commands.Contains(topic))
            throw new CliUsageException($"Unknown command '{arguments.Positional[0]}'. Run 'cloudlogin help' to see the commands.");

        await environment.Out.WriteLineAsync(CliHelp.For(topic ?? "help"));
        return CliExitCodes.Success;
    }

    private static async Task<int> VersionAsync(CliEnvironment environment)
    {
        string version = typeof(CloudLoginCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? typeof(CloudLoginCli).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        await environment.Out.WriteLineAsync($"cloudlogin {version}");
        return CliExitCodes.Success;
    }

    private static async Task<int> LoginAsync(CliArguments arguments, CliEnvironment environment, CancellationToken cancellationToken)
    {
        string applicationUrl = ApplicationUrl(arguments, environment);
        string scheme = Scheme(arguments, environment);
        ISignInDriver driver = CreateDriver(arguments, environment, allowPaste: true);
        SessionStore store = Store(arguments, environment);

        CookieContainer applicationCookies = new();
        CookieContainer browserCookies = new();

        using BrowserSession browser = new(environment.CreateTransport(), browserCookies, environment.RequestTimeout);
        using ApplicationClient application = new(applicationUrl, environment.CreateTransport(), applicationCookies, environment.RequestTimeout);

        NativeSignInResult result = await new NativeSignIn(applicationUrl, scheme).RunAsync(driver, browser, application, cancellationToken);
        CliUser user = new(result.User.Id, result.User.DisplayName, result.User.PrimaryEmailAddress?.Input);

        await store.SaveAsync(new CliSession(applicationUrl, scheme, user, CliSession.Capture(applicationCookies), CliSession.Capture(browserCookies), DateTimeOffset.UtcNow), cancellationToken);

        await environment.Out.WriteLineAsync($"Signed in as {Describe(user)} at {applicationUrl}.");
        await environment.Out.WriteLineAsync($"Session saved to {store.Path} ({store.Description}).");
        return CliExitCodes.Success;
    }

    private static async Task<int> StatusAsync(CliArguments arguments, CliEnvironment environment, CancellationToken cancellationToken)
    {
        (SessionStore store, CliSession? session, int failure) = await LoadAsync(arguments, environment, cancellationToken);

        if (session is null)
            return failure;

        using ApplicationClient application = new(session.ApplicationUrl, environment.CreateTransport(), CliSession.Restore(session.ApplicationCookies), environment.RequestTimeout);
        CloudLoginNativeTokenResult token = await application.Native.GetTokenAsync(arguments.Get("audience"), cancellationToken);

        if (arguments.Flag("json"))
            await environment.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                application = session.ApplicationUrl,
                user = session.User,
                status = token.Status.ToString(),
                audience = token.Audience,
                expiresIn = token.ExpiresIn
            }, JsonOutput));

        return await ReportTokenAsync(token, session, store, application, environment, arguments.Flag("json"), cancellationToken, describe: () =>
            $"Signed in as {Describe(session.User)} at {session.ApplicationUrl}. The session is valid; a token for {token.Audience} lasts {token.ExpiresIn} s.");
    }

    private static async Task<int> TokenAsync(CliArguments arguments, CliEnvironment environment, CancellationToken cancellationToken)
    {
        (SessionStore store, CliSession? session, int failure) = await LoadAsync(arguments, environment, cancellationToken);

        if (session is null)
            return failure;

        using ApplicationClient application = new(session.ApplicationUrl, environment.CreateTransport(), CliSession.Restore(session.ApplicationCookies), environment.RequestTimeout);
        CloudLoginNativeTokenResult token = await application.Native.GetTokenAsync(arguments.Get("audience"), cancellationToken);

        if (token.Status == CloudLoginNativeTokenStatuses.Issued)
        {
            IReadOnlyDictionary<string, string> claims = CliJwt.ReadClaims(token.AccessToken!);

            if (arguments.Flag("json"))
                await environment.Out.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    audience = token.Audience,
                    expiresIn = token.ExpiresIn,
                    claims,
                    accessToken = arguments.Flag("show-token") ? token.AccessToken : null
                }, JsonOutput));
            else
            {
                await environment.Out.WriteLineAsync($"Token for {token.Audience}, valid for {token.ExpiresIn} s.");

                foreach ((string name, string value) in claims)
                    await environment.Out.WriteLineAsync($"  {name}: {value}");

                if (arguments.Flag("show-token"))
                    await environment.Out.WriteLineAsync(token.AccessToken);
            }
        }

        return await ReportTokenAsync(token, session, store, application, environment, quiet: true, cancellationToken, describe: () => string.Empty);
    }

    private static async Task<int> ReportTokenAsync(
        CloudLoginNativeTokenResult token,
        CliSession session,
        SessionStore store,
        ApplicationClient application,
        CliEnvironment environment,
        bool quiet,
        CancellationToken cancellationToken,
        Func<string> describe)
    {
        switch (token.Status)
        {
            case CloudLoginNativeTokenStatuses.Issued:
                await store.SaveAsync(session with { ApplicationCookies = CliSession.Capture(application.Cookies) }, cancellationToken);

                if (!quiet)
                    await environment.Out.WriteLineAsync(describe());

                return CliExitCodes.Success;

            case CloudLoginNativeTokenStatuses.SessionOver:
                store.Delete();
                await environment.Error.WriteLineAsync($"The session at {session.ApplicationUrl} has ended. Run 'cloudlogin login' to sign in again.");
                return CliExitCodes.Failed;

            default:
                await environment.Error.WriteLineAsync("The session could not be checked right now: the backend or CloudLogin did not answer. The saved session is kept; try again.");
                return CliExitCodes.Unavailable;
        }
    }

    private static async Task<int> CallAsync(CliArguments arguments, CliEnvironment environment, CancellationToken cancellationToken)
    {
        if (arguments.Positional.Count != 1)
            throw new CliUsageException("'call' takes one path, for example: cloudlogin call api/profile");

        string path = arguments.Positional[0];

        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute) && absolute.Scheme is "http" or "https")
            throw new CliUsageException("'call' takes a path on the application, not a full address; the session is only ever sent to the application it belongs to.");

        HttpMethod method = new((arguments.Get("method") ?? (arguments.Has("body") ? "POST" : "GET")).ToUpperInvariant());

        if (method.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD"))
            throw new CliUsageException($"'--method {method.Method}' is not supported.");

        (SessionStore store, CliSession? session, int failure) = await LoadAsync(arguments, environment, cancellationToken);

        if (session is null)
            return failure;

        using ApplicationClient application = new(session.ApplicationUrl, environment.CreateTransport(), CliSession.Restore(session.ApplicationCookies), environment.RequestTimeout);
        using HttpRequestMessage request = new(method, path.TrimStart('/'));

        if (arguments.Get("body") is { } body)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await application.Http.SendAsync(request, cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        await environment.Out.WriteLineAsync($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        if (content.Length > 0)
            await environment.Out.WriteLineAsync(content.Length <= 8000 ? content : content[..8000] + "…");

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            store.Delete();
            await environment.Error.WriteLineAsync("The session has ended. Run 'cloudlogin login' to sign in again.");
            return CliExitCodes.Failed;
        }

        await store.SaveAsync(session with { ApplicationCookies = CliSession.Capture(application.Cookies) }, cancellationToken);
        return response.IsSuccessStatusCode ? CliExitCodes.Success : CliExitCodes.Failed;
    }

    private static async Task<int> LogoutAsync(CliArguments arguments, CliEnvironment environment, CancellationToken cancellationToken)
    {
        (SessionStore store, CliSession? session, int failure) = await LoadAsync(arguments, environment, cancellationToken);

        if (session is null)
            return failure;

        using ApplicationClient application = new(session.ApplicationUrl, environment.CreateTransport(), CliSession.Restore(session.ApplicationCookies), environment.RequestTimeout);
        CloudLoginNativeSessionEndResults result = await application.Native.EndSessionAsync(cancellationToken);

        switch (result)
        {
            case CloudLoginNativeSessionEndResults.Ended:
                store.Delete();
                await environment.Out.WriteLineAsync("Signed out. The session ended at the application and at CloudLogin, for every application that shared it.");
                return CliExitCodes.Success;

            case CloudLoginNativeSessionEndResults.NoSession:
                store.Delete();
                await environment.Out.WriteLineAsync("There was no session left to end. The saved session was removed.");
                return CliExitCodes.Success;

            case CloudLoginNativeSessionEndResults.Refused:
                await environment.Error.WriteLineAsync("The application refused to end the session.");
                return CliExitCodes.Failed;

            default:
                await environment.Error.WriteLineAsync("The application or CloudLogin could not be reached, so nothing changed. Run 'cloudlogin logout' again.");
                return CliExitCodes.Unavailable;
        }
    }

    private static async Task<int> ScenarioAsync(CliArguments arguments, CliEnvironment environment, CancellationToken cancellationToken)
    {
        string applicationUrl = ApplicationUrl(arguments, environment);
        string scheme = Scheme(arguments, environment);
        ISignInDriver driver = CreateDriver(arguments, environment, allowPaste: false);
        int parallel = arguments.Integer("parallel", 8, 1, 64);
        bool json = arguments.Flag("json");

        if (!json)
        {
            await environment.Out.WriteLineAsync($"CloudLogin scenario against {applicationUrl} (scheme '{scheme}', sign-in: {driver.Name})");
            await environment.Out.WriteLineAsync();
        }

        ScenarioRunner runner = new(environment, applicationUrl, scheme, () => driver, parallel);
        IReadOnlyList<ScenarioStepResult> results = await runner.RunAsync(step =>
        {
            if (json)
                return;

            string label = step.Status switch
            {
                ScenarioStepStatuses.Passed => "PASS",
                ScenarioStepStatuses.Failed => "FAIL",
                _ => "SKIP"
            };

            environment.Out.WriteLine($"  {label}  {step.Number,2}  {step.Name} — {step.Detail} ({step.Duration.TotalMilliseconds:0} ms)");
        }, cancellationToken);

        int passed = results.Count(step => step.Status == ScenarioStepStatuses.Passed);
        int failed = results.Count(step => step.Status == ScenarioStepStatuses.Failed);
        int skipped = results.Count(step => step.Status == ScenarioStepStatuses.Skipped);

        if (json)
            await environment.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                application = applicationUrl,
                passed,
                failed,
                skipped,
                steps = results.Select(step => new { step.Number, step.Name, status = step.Status.ToString(), step.Detail, milliseconds = (int)step.Duration.TotalMilliseconds })
            }, JsonOutput));
        else
        {
            await environment.Out.WriteLineAsync();
            await environment.Out.WriteLineAsync($"{passed} passed, {failed} failed, {skipped} skipped.");
        }

        return failed == 0 && skipped == 0 ? CliExitCodes.Success : CliExitCodes.Failed;
    }

    private static ISignInDriver CreateDriver(CliArguments arguments, CliEnvironment environment, bool allowPaste)
    {
        string mode = (arguments.Get("mode") ?? environment.GetEnvironmentVariable(ModeVariable) ?? (allowPaste ? "paste" : "testmode")).ToLowerInvariant();

        switch (mode)
        {
            case "testmode":
                return new TestModeSignInDriver(arguments.Get("test-user"), arguments.Flag("create-test-user"));

            case "password":
                string email = arguments.Require("email", environment.GetEnvironmentVariable, EmailVariable);
                string? password = environment.GetEnvironmentVariable(PasswordVariable);

                if (string.IsNullOrEmpty(password))
                {
                    environment.Error.Write("Password: ");
                    environment.Error.Flush();
                    password = environment.In.ReadLine();
                }

                return string.IsNullOrEmpty(password)
                    ? throw new CliUsageException($"A password is needed: set {PasswordVariable} or type it when asked. It is never taken as an option, so it stays out of the shell history.")
                    : new PasswordSignInDriver(email, password);

            case "paste" when allowPaste:
                return new PasteSignInDriver(environment.Out, environment.In, arguments.Flag("no-open") ? null : environment.OpenBrowser);

            case "paste":
                throw new CliUsageException("The scenario signs in several times, so it needs '--mode testmode' or '--mode password'.");

            default:
                throw new CliUsageException($"Unknown sign-in mode '{mode}'. Use paste, testmode or password.");
        }
    }

    private static string ApplicationUrl(CliArguments arguments, CliEnvironment environment)
    {
        string value = arguments.Require("app", environment.GetEnvironmentVariable, ApplicationVariable).Trim();

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url) || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            throw new CliUsageException($"'{value}' is not an application address. Give its backend's base address, for example https://app.example.com.");

        if (url.Scheme == "http" && !url.IsLoopback)
            throw new CliUsageException("An application address must use https; http is only accepted on this computer (localhost).");

        return url.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string Scheme(CliArguments arguments, CliEnvironment environment)
    {
        string value = arguments.Require("scheme", environment.GetEnvironmentVariable, SchemeVariable).Trim().TrimEnd(':', '/');

        if (value.Length == 0 || !char.IsAsciiLetter(value[0]) || !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.'))
            throw new CliUsageException($"'{value}' is not a URL scheme. Give the scheme the application registers, for example blusky.");

        if (value.Equals("http", StringComparison.OrdinalIgnoreCase) || value.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("The callback scheme is the application's own scheme, never http or https.");

        return value.ToLowerInvariant();
    }

    private static SessionStore Store(CliArguments arguments, CliEnvironment environment) =>
        new(arguments.Get("session") ?? environment.GetEnvironmentVariable(SessionVariable) ?? environment.DefaultSessionPath, environment.Protector);

    private static async Task<(SessionStore Store, CliSession? Session, int Failure)> LoadAsync(CliArguments arguments, CliEnvironment environment, CancellationToken cancellationToken)
    {
        SessionStore store = Store(arguments, environment);
        SessionLoadResult loaded = await store.LoadAsync(cancellationToken);

        switch (loaded.Status)
        {
            case SessionLoadStatuses.Loaded:
                return (store, loaded.Session, CliExitCodes.Success);

            case SessionLoadStatuses.Missing:
                await environment.Error.WriteLineAsync($"Not signed in: there is no saved session at {store.Path}. Run 'cloudlogin login' first.");
                return (store, null, CliExitCodes.Failed);

            default:
                await environment.Error.WriteLineAsync($"The saved session at {store.Path} could not be read (it may belong to another user or be damaged). Run 'cloudlogin login' again.");
                return (store, null, CliExitCodes.Failed);
        }
    }

    internal static string Describe(CliUser? user) =>
        user is null ? "an unknown person"
        : !string.IsNullOrWhiteSpace(user.DisplayName) && !string.IsNullOrWhiteSpace(user.Email) ? $"{user.DisplayName} ({user.Email})"
        : user.DisplayName ?? user.Email ?? user.Id.ToString();
}
