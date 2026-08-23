using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public sealed record CloudLoginEvent
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public int Version { get; init; } = 1;
    public required string Operation { get; init; }
    public required JsonElement Payload { get; init; }

    public static CloudLoginEvent Create<T>(
        string eventType,
        string entityType,
        Guid entityId,
        string operation,
        T payload) => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        EventType = eventType,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        Timestamp = DateTimeOffset.UtcNow,
        Operation = operation,
        Payload = JsonSerializer.SerializeToElement(
            payload,
            CloudLoginSerialization.Options)
    };
}
