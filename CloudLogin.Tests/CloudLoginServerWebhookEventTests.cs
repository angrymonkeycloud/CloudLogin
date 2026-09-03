namespace AngryMonkey.CloudLogin.Tests;

public class CloudLoginServerWebhookEventTests
{
    [Fact]
    public async Task User_mutations_publish_created_updated_and_deleted_events()
    {
        RecordingPublisher publisher = new();
        LoginTestFixture fixture = new(eventPublisher: publisher);
        CloudUser user = new() { Id = Guid.NewGuid(), DisplayName = "Webhook User" };

        await fixture.Server.CreateUser(user);
        user.DisplayName = "Updated User";
        await fixture.Server.UpdateUser(user);
        await fixture.Server.DeleteUser(user.Id);

        Assert.Equal(
            ["User.Created", "User.Updated", "User.Deleted"],
            publisher.Events.Select(item => item.EventType));
        Assert.All(publisher.Events, cloudEvent =>
        {
            Assert.Equal("User", cloudEvent.EntityType);
            Assert.Equal(user.Id.ToString(), cloudEvent.EntityId);
            Assert.Equal(1, cloudEvent.Version);
            Assert.True(cloudEvent.Payload.TryGetProperty("Id", out _));
        });
    }

    private sealed class RecordingPublisher : ICloudLoginEventPublisher
    {
        public List<CloudLoginEvent> Events { get; } = [];

        public Task PublishAsync(
            CloudLoginEvent cloudEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(cloudEvent);
            return Task.CompletedTask;
        }
    }
}
