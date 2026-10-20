using AngryMonkey.CloudLogin.Native;

namespace AngryMonkey.CloudLogin.Cli;

public enum CliConnectionStatuses
{
    Connected,
    NotSignedIn,
    SessionOver,
    Unavailable
}

/// <summary>
/// A saved session opened for a command: the application's own client, and clients for the other services the application lets the
/// user call, each carrying an access token minted for that service from this session. Disposing it keeps a renewed session cookie.
/// </summary>
public sealed class CliSessionConnection : IAsyncDisposable
{
    private readonly SessionStore _store;
    private readonly Func<HttpMessageHandler> _createTransport;
    private readonly TimeSpan _timeout;
    private readonly List<HttpClient> _services = [];
    private bool _ended;

    private CliSessionConnection(SessionStore store, CliSession session, Func<HttpMessageHandler> createTransport, TimeSpan timeout)
    {
        _store = store;
        _createTransport = createTransport;
        _timeout = timeout;
        Session = session;
        Application = new ApplicationClient(session.ApplicationUrl, createTransport(), CliSession.Restore(session.ApplicationCookies), timeout);
    }

    public CliSession Session { get; }
    public ApplicationClient Application { get; }

    /// <summary>The saved session, or why there is none to use.</summary>
    public static async Task<(CliConnectionStatuses Status, CliSessionConnection? Connection)> OpenAsync(
        SessionStore store,
        Func<HttpMessageHandler> createTransport,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(createTransport);

        SessionLoadResult loaded = await store.LoadAsync(cancellationToken);

        return loaded.Session is { } session
            ? (CliConnectionStatuses.Connected, new CliSessionConnection(store, session, createTransport, timeout))
            : (CliConnectionStatuses.NotSignedIn, null);
    }

    /// <summary>
    /// A client for the service at <paramref name="serviceUrl"/>, whose requests carry a token for <paramref name="audience"/>. The token is
    /// asked for first, so a session that has ended is reported as such instead of surfacing later as the service's 401.
    /// </summary>
    public async Task<(CliConnectionStatuses Status, HttpClient? Client)> ConnectAsync(Uri serviceUrl, string audience, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        CloudLoginNativeTokenResult token = await Application.Native.GetTokenAsync(audience, cancellationToken);

        switch (token.Status)
        {
            case CloudLoginNativeTokenStatuses.Issued:
                HttpClient client = new(new CloudLoginBearerTokenHandler(Application.Http, audience) { InnerHandler = _createTransport() })
                {
                    BaseAddress = new Uri(serviceUrl.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/"),
                    Timeout = _timeout
                };
                _services.Add(client);
                return (CliConnectionStatuses.Connected, client);

            case CloudLoginNativeTokenStatuses.SessionOver:
                EndSession();
                return (CliConnectionStatuses.SessionOver, null);

            default:
                return (CliConnectionStatuses.Unavailable, null);
        }
    }

    /// <summary>Forgets the saved session: the application has ended it.</summary>
    public void EndSession()
    {
        _ended = true;
        _store.Delete();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (HttpClient service in _services)
            service.Dispose();

        try
        {
            if (!_ended)
                await _store.SaveAsync(Session with { ApplicationCookies = CliSession.Capture(Application.Cookies) });
        }
        finally
        {
            Application.Dispose();
        }
    }
}
