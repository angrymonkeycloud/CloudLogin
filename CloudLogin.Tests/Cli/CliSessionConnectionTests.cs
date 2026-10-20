using System.Net;
using System.Net.Http.Headers;
using AngryMonkey.CloudLogin.Cli;

namespace AngryMonkey.CloudLogin.Tests.Cli;

/// <summary>
/// Another command-line tool opens the session the CLI saved and calls a second service with a token minted for it, the way the native
/// application calls its other services.
/// </summary>
public sealed class CliSessionConnectionTests : IDisposable
{
    private readonly CliHarness _harness = new();

    [Fact]
    public async Task Without_a_saved_session_nothing_is_opened()
    {
        (CliConnectionStatuses status, CliSessionConnection? connection) = await CliSessionConnection.OpenAsync(_harness.Store, () => new ScriptedTransport(), TimeSpan.FromSeconds(5));

        Assert.Equal(CliConnectionStatuses.NotSignedIn, status);
        Assert.Null(connection);
    }

    [Fact]
    public async Task A_service_client_carries_a_token_minted_for_its_audience()
    {
        await _harness.SaveSessionAsync();
        ScriptedTransport transport = new ScriptedTransport()
            .On(HttpMethod.Get, "app.test/auth/token", request => ScriptedTransport.Respond(HttpStatusCode.OK,
                "{\"access_token\":\"token-" + request.Query("audience") + "\",\"expires_in\":600,\"audience\":\"" + request.Query("audience") + "\"}"))
            .On(HttpMethod.Get, "portal.test/api/records", request => ScriptedTransport.Respond(HttpStatusCode.OK, $$"""{"auth":"{{request.Header("Authorization")}}"}"""));

        (CliConnectionStatuses opened, CliSessionConnection? connection) = await CliSessionConnection.OpenAsync(_harness.Store, () => new NonDisposingHandler(transport), TimeSpan.FromSeconds(5));
        Assert.Equal(CliConnectionStatuses.Connected, opened);

        await using (connection!)
        {
            (CliConnectionStatuses status, HttpClient? portal) = await connection.ConnectAsync(new Uri("https://portal.test/"), "portal");

            Assert.Equal(CliConnectionStatuses.Connected, status);
            string body = await portal!.GetStringAsync("api/records");

            Assert.Contains("Bearer token-portal", body);
            Assert.Equal("https://portal.test/", portal.BaseAddress!.ToString());
        }

        RecordedRequest tokenRequest = transport.Requests.First(request => request.Uri.AbsolutePath == "/auth/token");
        Assert.Contains("app.session=session-1", tokenRequest.Header("Cookie"));
        Assert.DoesNotContain(transport.Requests, request => request.Uri.Host == "portal.test" && request.Header("Cookie") is not null);
    }

    [Fact]
    public async Task An_ended_session_is_forgotten_instead_of_saved_again()
    {
        await _harness.SaveSessionAsync();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/auth/token", HttpStatusCode.Unauthorized);

        (_, CliSessionConnection? connection) = await CliSessionConnection.OpenAsync(_harness.Store, () => new NonDisposingHandler(transport), TimeSpan.FromSeconds(5));

        await using (connection!)
        {
            (CliConnectionStatuses status, HttpClient? client) = await connection.ConnectAsync(new Uri("https://portal.test"), "portal");

            Assert.Equal(CliConnectionStatuses.SessionOver, status);
            Assert.Null(client);
        }

        Assert.Equal(SessionLoadStatuses.Missing, (await _harness.Store.LoadAsync()).Status);
    }

    [Fact]
    public async Task An_unreachable_application_keeps_the_session()
    {
        await _harness.SaveSessionAsync();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/auth/token", HttpStatusCode.ServiceUnavailable);

        (_, CliSessionConnection? connection) = await CliSessionConnection.OpenAsync(_harness.Store, () => new NonDisposingHandler(transport), TimeSpan.FromSeconds(5));

        await using (connection!)
        {
            (CliConnectionStatuses status, _) = await connection.ConnectAsync(new Uri("https://portal.test"), "portal");
            Assert.Equal(CliConnectionStatuses.Unavailable, status);
        }

        Assert.Equal(SessionLoadStatuses.Loaded, (await _harness.Store.LoadAsync()).Status);
    }

    [Fact]
    public async Task A_renewed_session_cookie_is_kept_for_the_next_command()
    {
        await _harness.SaveSessionAsync();
        ScriptedTransport transport = new ScriptedTransport().On(HttpMethod.Get, "app.test/auth/token", _ =>
        {
            HttpResponseMessage response = ScriptedTransport.Respond(HttpStatusCode.OK, """{"access_token":"t","expires_in":600,"audience":"portal"}""");
            response.Headers.Add("Set-Cookie", "app.session=session-2; path=/; secure; httponly");
            return response;
        });

        (_, CliSessionConnection? connection) = await CliSessionConnection.OpenAsync(_harness.Store, () => new NonDisposingHandler(transport), TimeSpan.FromSeconds(5));

        await using (connection!)
            Assert.Equal(CliConnectionStatuses.Connected, (await connection.ConnectAsync(new Uri("https://portal.test"), "portal")).Status);

        CliSession saved = await _harness.LoadSessionAsync();
        Assert.Contains(saved.ApplicationCookies, cookie => cookie.Name == "app.session" && cookie.Value == "session-2");
    }

    public void Dispose() => _harness.Dispose();
}
