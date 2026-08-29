using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Services;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginWebhookPublisherTests
{
    [Fact]
    public async Task Publisher_signs_versioned_payload_and_retries_failures()
    {
        RecordingHandler handler = new(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.NoContent);
        CloudLoginWebConfiguration configuration = Configuration(
            "Workspace.Updated");
        CloudLoginEvent cloudEvent = CloudLoginEvent.Create(
            "Workspace.Updated",
            "Workspace",
            Guid.NewGuid(),
            "Updated",
            new { Name = "Acme" });
        CloudLoginWebhookPublisher publisher = new(
            new HttpClient(handler),
            configuration);

        await publisher.PublishAsync(cloudEvent);

        Assert.Equal(2, handler.Requests.Count);
        RecordedRequest delivered = handler.Requests[^1];
        Assert.Equal(cloudEvent.EventId, delivered.EventId);
        Assert.Equal(
            cloudEvent.Timestamp.ToUnixTimeSeconds().ToString(),
            delivered.Timestamp);
        Assert.True(CloudLoginWebhookPublisher.Verify(
            delivered.Body,
            delivered.Signature,
            configuration.Webhooks[0].Secret));

        CloudLoginEvent parsed = JsonSerializer.Deserialize<CloudLoginEvent>(
            delivered.Body,
            CloudLoginSerialization.Options)!;
        Assert.Equal(1, parsed.Version);
        Assert.Equal("Workspace", parsed.EntityType);
        Assert.Equal("Updated", parsed.Operation);
    }

    [Fact]
    public async Task Publisher_only_delivers_subscribed_event_types()
    {
        RecordingHandler handler = new(HttpStatusCode.NoContent);
        CloudLoginWebhookPublisher publisher = new(
            new HttpClient(handler),
            Configuration("User.Updated"));

        await publisher.PublishAsync(CloudLoginEvent.Create(
            "Workspace.Updated",
            "Workspace",
            Guid.NewGuid(),
            "Updated",
            new { }));

        Assert.Empty(handler.Requests);
    }

    private static CloudLoginWebConfiguration Configuration(string eventType)
    {
        CloudLoginWebConfiguration configuration = new();
        configuration.Webhooks.Add(new CloudLoginWebhookRegistration
        {
            Application = "Tests",
            Url = new Uri("https://consumer.example/webhooks/cloudlogin"),
            Secret = "01234567890123456789012345678901",
            Events = [eventType]
        });
        return configuration;
    }

    private sealed record RecordedRequest(
        string Body,
        string EventId,
        string Timestamp,
        string Signature);

    private sealed class RecordingHandler(params HttpStatusCode[] statuses)
        : HttpMessageHandler
    {
        private int _attempt;
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.GetValues("X-CloudLogin-Event-Id").Single(),
                request.Headers.GetValues("X-CloudLogin-Timestamp").Single(),
                request.Headers.GetValues("X-CloudLogin-Signature").Single()));

            HttpStatusCode status = statuses[
                Math.Min(_attempt, statuses.Length - 1)];
            _attempt++;
            return new HttpResponseMessage(status);
        }
    }

    [Fact]
    public void Dependency_injection_resolves_configuration_publisher_and_registries()
    {
        CloudLoginWebConfiguration configuration = new();
        ServiceCollection services = new();
        services.AddCloudLoginWeb(configuration);
        services.AddCloudLoginAccountRegistry();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(
            configuration,
            scope.ServiceProvider.GetRequiredService<CloudLoginWebConfiguration>());
        Assert.IsType<CloudLoginWebhookPublisher>(
            scope.ServiceProvider.GetRequiredService<ICloudLoginEventPublisher>());
        Assert.IsType<WorkspaceRegistry>(
            scope.ServiceProvider.GetRequiredService<ICloudLoginWorkspaceRegistry>());
    }
}
