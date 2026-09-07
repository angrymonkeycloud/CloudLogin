using AngryMonkey.CloudLogin.API.V3;
using AngryMonkey.CloudLogin.Aspire;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Headers;

namespace AngryMonkey.CloudLogin.Tests;

public class ActivityMapTests
{
    [Fact]
    public void MapsReferenceBindsSharedConfiguration()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Maps:AzureSubscriptionKey"] = "test-key" });
        Assert.Equal("test-key", builder.ReadCloudLoginConfiguration().MapsSubscriptionKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryOnlyOffersPreviewWhenMapsIsConfigured(bool configured)
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        fixture.Configuration.MapsSubscriptionKey = configured ? "test-key" : null;
        await fixture.SecurityStore.RecordSignIn(user.Id, new() { Latitude = 33.89, Longitude = 35.5 });
        CloudLoginHistoryEntry entry = Assert.Single(await fixture.Server.GetMyLoginHistory());
        Assert.Equal(configured, entry.MapImageUrl is not null);
    }

    [Fact]
    public async Task MapUsesOnlyOwnedHistory_AndKeepsKeyOutOfUrl()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        fixture.AuthenticateAs(user);
        fixture.Configuration.MapsSubscriptionKey = "test-key";
        CloudLoginHistoryEntry entry = new() { Latitude = 33.89, Longitude = 35.5 };
        await fixture.SecurityStore.RecordSignIn(user.Id, entry);
        MapTransport transport = new();
        V3ActivityMapController controller = new(fixture.Configuration, fixture.Server, transport)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        Assert.IsType<NotFoundResult>(await controller.GetMap(Guid.NewGuid(), default));
        Assert.Equal(0, transport.Calls);
        FileContentResult result = Assert.IsType<FileContentResult>(await controller.GetMap(entry.Id, default));
        Assert.Equal("image/png", result.ContentType);
        Assert.Contains("center=35.5,33.89", transport.Url);
        Assert.DoesNotContain("test-key", transport.Url);
        Assert.Equal("test-key", transport.Key);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    private sealed class MapTransport : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public string Url { get; private set; } = "";
        public string Key { get; private set; } = "";
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri!.AbsoluteUri;
            Key = request.Headers.GetValues("subscription-key").Single();
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent([137, 80, 78, 71]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }
    }
}