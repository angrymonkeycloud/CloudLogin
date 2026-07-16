using System.Net;
using System.Net.Http.Headers;

namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginClientTests
{
    [Fact]
    public async Task CompleteLoginRedirect_ReadsPlainTextRedirectTarget()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("/Account")
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
            }
        });
        CloudLoginClient client = CreateClient(handler);

        string target = await client.CompleteLoginRedirect();

        Assert.Equal("/Account", target);
        Assert.Equal(
            "https://login.example/CloudLogin/Login/Complete?isMobileApp=false",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task CompleteLoginRedirect_PreservesExternalRedirectTarget()
    {
        const string redirect = "https://portal.example/auth/login?requestId=7c7ebec4-c431-4ec6-ac8c-667f1407c12a";
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(redirect)
        });
        CloudLoginClient client = CreateClient(handler);

        string target = await client.CompleteLoginRedirect("https://portal.example/auth/login");

        Assert.Equal(redirect, target);
        Assert.Contains("referer=https%3a%2f%2fportal.example%2fauth%2flogin", handler.LastRequestUri?.Query);
    }

    [Fact]
    public async Task CompleteLoginRedirect_RejectsEmptyResponse()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("   ")
        });
        CloudLoginClient client = CreateClient(handler);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteLoginRedirect());

        Assert.Equal("CloudLogin returned an empty redirect target.", exception.Message);
    }

    private static CloudLoginClient CreateClient(HttpMessageHandler handler) => new()
    {
        HttpServer = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://login.example/")
        }
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }
}
