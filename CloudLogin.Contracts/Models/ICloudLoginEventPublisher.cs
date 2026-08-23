namespace AngryMonkey.CloudLogin;

public interface ICloudLoginEventPublisher
{
    Task PublishAsync(
        CloudLoginEvent cloudEvent,
        CancellationToken cancellationToken = default);
}
