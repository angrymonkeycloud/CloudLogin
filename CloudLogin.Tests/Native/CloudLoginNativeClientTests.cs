using System.Net;
using System.Text.Json;
using AngryMonkey.CloudLogin.Native;
using AngryMonkey.CloudLogin.Tests.Cli;

namespace AngryMonkey.CloudLogin.Tests.Native;

public class CloudLoginNativeClientTests
{
    private static (CloudLoginNativeClient Client, ScriptedTransport Transport) Create(Action<ScriptedTransport>? routes = null)
    {
        ScriptedTransport transport = new();
        routes?.Invoke(transport);
        return (new CloudLoginNativeClient(new HttpClient(new NonDisposingHandler(transport)) { BaseAddress = new Uri("https://app.test/") }), transport);
    }

    [Fact]
    public void TheClientNeedsAnHttpClient() => Assert.Throws<ArgumentNullException>(() => new CloudLoginNativeClient(null!));

    // ── Exchange ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Exchange_PostsTheHandoffAndVerifier_AndReturnsThePerson()
    {
        CloudUser person = new() { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" };
        (CloudLoginNativeClient client, ScriptedTransport transport) = Create(routes => routes.On(HttpMethod.Post, "app.test/auth/native/exchange", HttpStatusCode.OK, JsonSerializer.Serialize(person, CloudLoginSerialization.Options)));

        CloudUser? user = await client.ExchangeAsync("handoff-1", "verifier-1");

        Assert.Equal(person.Id, user!.Id);
        RecordedRequest request = Assert.Single(transport.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body);
        Assert.Equal("handoff-1", body.RootElement.GetProperty("handoff").GetString());
        Assert.Equal("verifier-1", body.RootElement.GetProperty("verifier").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Exchange_RefusedForAnyReason_IsNull(HttpStatusCode status)
    {
        (CloudLoginNativeClient client, _) = Create(routes => routes.On(HttpMethod.Post, "app.test/auth/native/exchange", status, "{\"error\":\"invalid_grant\"}"));
        Assert.Null(await client.ExchangeAsync("handoff-1", "verifier-1"));
    }

    [Theory]
    [InlineData("", "verifier")]
    [InlineData("handoff", "")]
    [InlineData(" ", "verifier")]
    public async Task Exchange_NeedsBothValues(string handoff, string verifier)
    {
        (CloudLoginNativeClient client, ScriptedTransport transport) = Create();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.ExchangeAsync(handoff, verifier));
        Assert.Empty(transport.Requests);
    }

    // ── Ending the session ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.NoContent, CloudLoginNativeSessionEndResults.Ended)]
    [InlineData(HttpStatusCode.OK, CloudLoginNativeSessionEndResults.Ended)]
    [InlineData(HttpStatusCode.Unauthorized, CloudLoginNativeSessionEndResults.NoSession)]
    [InlineData(HttpStatusCode.Forbidden, CloudLoginNativeSessionEndResults.Refused)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CloudLoginNativeSessionEndResults.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, CloudLoginNativeSessionEndResults.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, CloudLoginNativeSessionEndResults.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, CloudLoginNativeSessionEndResults.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, CloudLoginNativeSessionEndResults.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, CloudLoginNativeSessionEndResults.Unavailable)]
    public async Task EndSession_MapsEachAnswer(HttpStatusCode status, CloudLoginNativeSessionEndResults expected)
    {
        (CloudLoginNativeClient client, ScriptedTransport transport) = Create(routes => routes.On(HttpMethod.Post, "app.test/auth/native/logout", status));

        Assert.Equal(expected, await client.EndSessionAsync());
        Assert.Equal(HttpMethod.Post, Assert.Single(transport.Requests).Method);
    }

    [Theory]
    [InlineData("/auth/login?returnUrl=%2F", CloudLoginNativeSessionEndResults.NoSession)]
    [InlineData("https://app.test/auth/login", CloudLoginNativeSessionEndResults.NoSession)]
    [InlineData("/somewhere-else", CloudLoginNativeSessionEndResults.Unavailable)]
    public async Task EndSession_ARedirectToSignIn_MeansNoSession(string location, CloudLoginNativeSessionEndResults expected)
    {
        (CloudLoginNativeClient client, _) = Create(routes => routes.On(HttpMethod.Post, "app.test/auth/native/logout", _ => ScriptedTransport.Redirect(location)));
        Assert.Equal(expected, await client.EndSessionAsync());
    }

    [Fact]
    public async Task EndSession_WhenTheNetworkFails_IsUnavailable()
    {
        (CloudLoginNativeClient client, ScriptedTransport transport) = Create();
        transport.Fault = _ => new HttpRequestException("No route to host");

        Assert.Equal(CloudLoginNativeSessionEndResults.Unavailable, await client.EndSessionAsync());
    }

    [Fact]
    public async Task EndSession_WhenTheRequestTimesOut_IsUnavailable()
    {
        (CloudLoginNativeClient client, ScriptedTransport transport) = Create();
        transport.Fault = _ => new TaskCanceledException("timed out");

        Assert.Equal(CloudLoginNativeSessionEndResults.Unavailable, await client.EndSessionAsync());
    }

    [Fact]
    public async Task EndSession_WhenTheCallerCancels_IsCancelled()
    {
        (CloudLoginNativeClient client, _) = Create(routes => routes.On(HttpMethod.Post, "app.test/auth/native/logout", HttpStatusCode.NoContent));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EndSessionAsync(cancelled.Token));
    }

    [Fact]
    public async Task EndSession_SendsNoBrowserHeaders()
    {
        (CloudLoginNativeClient client, ScriptedTransport transport) = Create(routes => routes.On(HttpMethod.Post, "app.test/auth/native/logout", HttpStatusCode.NoContent));

        await client.EndSessionAsync();

        RecordedRequest request = Assert.Single(transport.Requests);
        Assert.Null(request.Header("Sec-Fetch-Site"));
        Assert.Null(request.Header("Origin"));
    }

    // ── Tokens ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_Issued_CarriesTheTokenLifetimeAndAudience()
    {
        (CloudLoginNativeClient client, _) = Create(routes => routes.On(HttpMethod.Get, "app.test/auth/token", HttpStatusCode.OK, """{"access_token":"abc","token_type":"Bearer","expires_in":599,"audience":"portal"}"""));

        CloudLoginNativeTokenResult result = await client.GetTokenAsync("portal");

        Assert.Equal(CloudLoginNativeTokenStatuses.Issued, result.Status);
        Assert.Equal("abc", result.AccessToken);
        Assert.Equal(599, result.ExpiresIn);
        Assert.Equal("portal", result.Audience);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("portal", "portal")]
    [InlineData("a b&c", "a b&c")]
    public async Task Token_AsksForTheAudience_OnlyWhenOneIsGiven(string? audience, string? expected)
    {
        (CloudLoginNativeClient client, ScriptedTransport transport) = Create(routes => routes.On(HttpMethod.Get, "app.test/auth/token", HttpStatusCode.OK, """{"access_token":"abc","expires_in":1}"""));

        await client.GetTokenAsync(audience);

        Assert.Equal(expected, Assert.Single(transport.Requests).Query("audience"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CloudLoginNativeTokenStatuses.SessionOver)]
    [InlineData(HttpStatusCode.Forbidden, CloudLoginNativeTokenStatuses.SessionOver)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CloudLoginNativeTokenStatuses.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, CloudLoginNativeTokenStatuses.Unavailable)]
    [InlineData(HttpStatusCode.NotImplemented, CloudLoginNativeTokenStatuses.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, CloudLoginNativeTokenStatuses.Unavailable)]
    public async Task Token_MapsEachAnswer(HttpStatusCode status, CloudLoginNativeTokenStatuses expected)
    {
        (CloudLoginNativeClient client, _) = Create(routes => routes.On(HttpMethod.Get, "app.test/auth/token", status));
        Assert.Equal(expected, (await client.GetTokenAsync()).Status);
    }

    [Fact]
    public async Task Token_ARedirectToSignIn_MeansTheSessionIsOver()
    {
        (CloudLoginNativeClient client, _) = Create(routes => routes.On(HttpMethod.Get, "app.test/auth/token", _ => ScriptedTransport.Redirect("/auth/login?returnUrl=%2Fauth%2Ftoken")));
        Assert.Equal(CloudLoginNativeTokenStatuses.SessionOver, (await client.GetTokenAsync()).Status);
    }

    [Theory]
    [InlineData("""{"access_token":"","expires_in":10}""")]
    [InlineData("""{"expires_in":10}""")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task Token_AnOkWithoutAToken_IsUnavailable_NotTheSessionEnding(string body)
    {
        (CloudLoginNativeClient client, _) = Create(routes => routes.On(HttpMethod.Get, "app.test/auth/token", HttpStatusCode.OK, body));
        Assert.Equal(CloudLoginNativeTokenStatuses.Unavailable, (await client.GetTokenAsync()).Status);
    }

    [Fact]
    public async Task Token_WhenTheNetworkFails_IsUnavailable()
    {
        (CloudLoginNativeClient client, ScriptedTransport transport) = Create();
        transport.Fault = _ => new HttpRequestException("Connection refused");

        Assert.Equal(CloudLoginNativeTokenStatuses.Unavailable, (await client.GetTokenAsync()).Status);
    }

    [Fact]
    public async Task Token_WhenTheCallerCancels_IsCancelled()
    {
        (CloudLoginNativeClient client, _) = Create(routes => routes.On(HttpMethod.Get, "app.test/auth/token", HttpStatusCode.OK, """{"access_token":"a","expires_in":1}"""));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetTokenAsync(cancellationToken: cancelled.Token));
    }
}
