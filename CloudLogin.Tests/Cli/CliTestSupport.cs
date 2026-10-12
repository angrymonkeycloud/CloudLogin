using System.Net;
using System.Text;
using System.Text.Json;
using AngryMonkey.CloudLogin.Cli;

namespace AngryMonkey.CloudLogin.Tests.Cli;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out string? value) ? value : null;

    public string? Query(string name) => Parse(Uri.Query).GetValueOrDefault(name);

    public string? Form(string name) => Parse(Body).GetValueOrDefault(name);

    private static Dictionary<string, string> Parse(string text)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string pair in text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            values.TryAdd(Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]), separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' ')));
        }

        return values;
    }
}

/// <summary>A fake network: requests are answered by the first matching route and every request is recorded.</summary>
internal sealed class ScriptedTransport : HttpMessageHandler
{
    private readonly List<(Func<RecordedRequest, bool> Match, Func<RecordedRequest, HttpResponseMessage> Respond)> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public Func<RecordedRequest, Exception?>? Fault { get; set; }

    public ScriptedTransport On(HttpMethod method, string hostAndPath, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _routes.Add((request => request.Method == method && $"{request.Uri.Host}{request.Uri.AbsolutePath}".Equals(hostAndPath, StringComparison.OrdinalIgnoreCase), respond));
        return this;
    }

    public ScriptedTransport On(HttpMethod method, string hostAndPath, HttpStatusCode status, string? body = null) =>
        On(method, hostAndPath, _ => Respond(status, body));

    public static HttpResponseMessage Respond(HttpStatusCode status, string? body = null, string contentType = "application/json")
    {
        HttpResponseMessage response = new(status);

        if (body is not null)
            response.Content = new StringContent(body, Encoding.UTF8, contentType);

        return response;
    }

    public static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Redirect)
    {
        HttpResponseMessage response = new(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    public int Count(HttpMethod method, string hostAndPath) =>
        Requests.Count(request => request.Method == method && $"{request.Uri.Host}{request.Uri.AbsolutePath}".Equals(hostAndPath, StringComparison.OrdinalIgnoreCase));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            headers[header.Key] = string.Join("; ", header.Value);

        if (request.Content is not null)
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                headers[header.Key] = string.Join("; ", header.Value);

        RecordedRequest recorded = new(request.Method, request.RequestUri!, headers, body);

        lock (Requests)
            Requests.Add(recorded);

        if (Fault?.Invoke(recorded) is { } fault)
            throw fault;

        foreach ((Func<RecordedRequest, bool> match, Func<RecordedRequest, HttpResponseMessage> respond) in _routes)
            if (match(recorded))
            {
                HttpResponseMessage response = respond(recorded);
                response.RequestMessage = request;
                return response;
            }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
    }
}

/// <summary>Shares one handler between many clients without any of them disposing it.</summary>
internal sealed class NonDisposingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override void Dispose(bool disposing)
    {
    }
}

/// <summary>Reversible, so a test can tell the file on disk was protected.</summary>
internal sealed class ScramblingProtector : ISessionProtector
{
    public string Description => "scrambled for tests";

    public byte[] Protect(byte[] data) => [.. data.Select(value => (byte)(value ^ 0x5A))];

    public byte[] Unprotect(byte[] data) => Protect(data);
}

/// <summary>Answers a read with whatever the test computes at that moment, so it can reply to what the CLI just printed.</summary>
internal sealed class ComputedReader(Func<string?> next) : TextReader
{
    public override string? ReadLine() => next();

    public override Task<string?> ReadLineAsync() => Task.FromResult(next());

    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => ValueTask.FromResult(next());
}

/// <summary>Runs the CLI with captured output, a session file of its own, and only the environment variables a test sets.</summary>
internal sealed class CliHarness : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cloudlogin-cli-tests", Guid.NewGuid().ToString("N"));

    public CliHarness() => Directory.CreateDirectory(_directory);

    public StringWriter Out { get; } = new();
    public StringWriter Error { get; } = new();
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);
    public List<string> Opened { get; } = [];
    public TextReader Input { get; set; } = new StringReader(string.Empty);
    public ISessionProtector Protector { get; set; } = new ScramblingProtector();
    public string SessionPath => Path.Combine(_directory, "session.json");
    public SessionStore Store => new(SessionPath, Protector);

    public CliEnvironment Environment(HttpMessageHandler transport) => new()
    {
        Out = Out,
        Error = Error,
        In = Input,
        CreateTransport = () => new NonDisposingHandler(transport),
        Protector = Protector,
        DefaultSessionPath = SessionPath,
        OpenBrowser = Opened.Add,
        GetEnvironmentVariable = name => Variables.GetValueOrDefault(name),
        RequestTimeout = TimeSpan.FromSeconds(15)
    };

    public Task<int> RunAsync(HttpMessageHandler transport, params string[] args) => CloudLoginCli.RunAsync(args, Environment(transport));

    public async Task SaveSessionAsync(string applicationUrl = "https://app.test", string cookieValue = "session-1", string scheme = "blusky")
    {
        CliCookie cookie = new("app.session", cookieValue, new Uri(applicationUrl).Host, "/", DateTimeOffset.UtcNow.AddDays(1), true, true);
        await Store.SaveAsync(new CliSession(applicationUrl, scheme, new CliUser(Guid.NewGuid(), "Ada Lovelace", "ada@example.com"), [cookie], [], DateTimeOffset.UtcNow));
    }

    public async Task<CliSession> LoadSessionAsync()
    {
        SessionLoadResult loaded = await Store.LoadAsync();
        Assert.Equal(SessionLoadStatuses.Loaded, loaded.Status);
        return loaded.Session!;
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }
}

/// <summary>
/// A scripted application backend and CloudLogin authority that behave like the real ones for the native flow: the login URL leads to the
/// authority, Test Mode and password sign-in complete back to the backend's callback, the backend hands off on the custom scheme with the
/// state it was given, and the exchange answers with a session cookie.
/// </summary>
internal sealed class FakeDeployment
{
    public const string App = "https://app.test";
    public const string Authority = "https://login.test";
    public const string Handoff = "handoff-123";
    public const string SessionCookie = "app.session";

    private string? _state;
    private string? _challenge;

    public ScriptedTransport Transport { get; } = new();
    public List<CloudUser> TestUsers { get; } = [new CloudUser { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" }];
    public string Scheme { get; set; } = "blusky";
    public string? ReturnState { get; set; }
    public string? CallbackError { get; set; }
    public HttpStatusCode ExchangeStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode SessionEndStatus { get; set; } = HttpStatusCode.NoContent;
    public bool TestModeEnabled { get; set; } = true;
    public string ExpectedPassword { get; set; } = "Valid#123456";
    public string? Verifier { get; private set; }
    public string IssuedCookie { get; set; } = "session-1";

    public FakeDeployment()
    {
        Transport
            .On(HttpMethod.Get, "app.test/auth/native/login", request =>
            {
                _state = request.Query("state");
                _challenge = request.Query("challenge");
                return ScriptedTransport.Redirect($"{Authority}/?referer=cltx%3Atx-1");
            })
            .On(HttpMethod.Get, "login.test/", HttpStatusCode.OK, "<html>sign in</html>")
            .On(HttpMethod.Get, "login.test/CloudLogin/User/GetTestUsers", _ => TestModeEnabled
                ? ScriptedTransport.Respond(HttpStatusCode.OK, JsonSerializer.Serialize(TestUsers, CloudLoginSerialization.Options))
                : ScriptedTransport.Respond(HttpStatusCode.InternalServerError))
            .On(HttpMethod.Post, "login.test/CloudLogin/Login/TestSignIn", request =>
                TestUsers.Any(user => user.Id.ToString() == request.Form("userId")) ? SignedInAtAuthority(HttpStatusCode.OK) : ScriptedTransport.Respond(HttpStatusCode.Unauthorized))
            .On(HttpMethod.Post, "login.test/CloudLogin/Login/PasswordRegistration", request =>
            {
                CloudUser created = new()
                {
                    Id = Guid.NewGuid(),
                    FirstName = request.Form("firstName"),
                    LastName = request.Form("lastName"),
                    IsTest = true,
                    Inputs = [new CloudLoginInput { Input = request.Form("input") ?? string.Empty, Format = CloudLoginInputFormat.EmailAddress, IsPrimary = true }]
                };
                TestUsers.Add(created);
                return ScriptedTransport.Respond(HttpStatusCode.OK, "{}");
            })
            .On(HttpMethod.Post, "login.test/CloudLogin/Login/PasswordSignIn", request =>
            {
                return request.Form("password") == ExpectedPassword
                    ? SignedInAtAuthority(HttpStatusCode.OK)
                    : ScriptedTransport.Respond(HttpStatusCode.BadRequest, "Invalid email or password.");
            })
            .On(HttpMethod.Get, "login.test/CloudLogin/Login/Complete", request =>
                request.Header("Cookie")?.Contains("authority=signed-in", StringComparison.Ordinal) == true
                    ? ScriptedTransport.Respond(HttpStatusCode.OK, JsonSerializer.Serialize($"{App}/auth/callback?code=code-1&state=backend-state"))
                    : ScriptedTransport.Respond(HttpStatusCode.Unauthorized))
            .On(HttpMethod.Get, "app.test/auth/callback", _ => CallbackError is { } error
                ? ScriptedTransport.Redirect($"{Scheme}://auth/callback?error={error}&state={Uri.EscapeDataString(ReturnState ?? _state ?? string.Empty)}")
                : ScriptedTransport.Redirect($"{Scheme}://auth/callback?handoff={Handoff}&state={Uri.EscapeDataString(ReturnState ?? _state ?? string.Empty)}"))
            .On(HttpMethod.Post, "app.test/auth/native/exchange", request =>
            {
                using JsonDocument body = JsonDocument.Parse(request.Body);
                Verifier = body.RootElement.GetProperty("handoff").GetString() == Handoff ? body.RootElement.GetProperty("verifier").GetString() : null;

                if (ExchangeStatus != HttpStatusCode.OK || Verifier is null || CloudLoginPkce.CreateChallenge(Verifier) != _challenge)
                    return ScriptedTransport.Respond(ExchangeStatus == HttpStatusCode.OK ? HttpStatusCode.BadRequest : ExchangeStatus, "{\"error\":\"invalid_grant\"}");

                HttpResponseMessage response = ScriptedTransport.Respond(HttpStatusCode.OK, JsonSerializer.Serialize(TestUsers[0], CloudLoginSerialization.Options));
                response.Headers.Add("Set-Cookie", $"{SessionCookie}={IssuedCookie}; path=/; secure; httponly; expires={DateTimeOffset.UtcNow.AddDays(30):R}");
                return response;
            })
            .On(HttpMethod.Get, "app.test/auth/token", request =>
            {
                if (request.Header("Cookie")?.Contains($"{SessionCookie}=", StringComparison.Ordinal) != true)
                    return ScriptedTransport.Respond(HttpStatusCode.Unauthorized);

                return TokenStatus == HttpStatusCode.OK
                    ? ScriptedTransport.Respond(HttpStatusCode.OK, $$"""{"access_token":"{{Jwt()}}","token_type":"Bearer","expires_in":600,"audience":"{{request.Query("audience") ?? "app"}}"}""")
                    : ScriptedTransport.Respond(TokenStatus);
            })
            .On(HttpMethod.Post, "app.test/auth/native/logout", request =>
                request.Header("Cookie")?.Contains($"{SessionCookie}=", StringComparison.Ordinal) == true ? ScriptedTransport.Respond(SessionEndStatus) : ScriptedTransport.Respond(HttpStatusCode.Unauthorized));
    }

    /// <summary>The person's own browser opened the login address: the backend now knows that sign-in's state and challenge.</summary>
    public string BrowserOpened(string loginUrl)
    {
        RecordedRequest opened = new(HttpMethod.Get, new Uri(loginUrl), new Dictionary<string, string>(), string.Empty);
        _state = opened.Query("state");
        _challenge = opened.Query("challenge");
        return $"{Scheme}://auth/callback?handoff={Handoff}&state={Uri.EscapeDataString(ReturnState ?? _state ?? string.Empty)}";
    }

    private static HttpResponseMessage SignedInAtAuthority(HttpStatusCode status)
    {
        HttpResponseMessage response = ScriptedTransport.Respond(status);
        response.Headers.Add("Set-Cookie", "authority=signed-in; path=/; secure; httponly");
        return response;
    }

    public static string Jwt(string subject = "user-1", string sessionId = "session-9")
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Encode("""{"alg":"ES256","typ":"JWT"}""")}.{Encode($$"""{"iss":"https://login.test","aud":"app","sub":"{{subject}}","sid":"{{sessionId}}","exp":1893456000}""")}.signature";
    }
}
