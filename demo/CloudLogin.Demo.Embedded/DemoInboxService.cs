using System.Collections.Concurrent;

namespace CloudLogin.Demo.Embedded;

public sealed class DemoInboxService
{
    public sealed record InboxEntry(string Address, string Code, DateTimeOffset SentAt);

    private readonly ConcurrentQueue<InboxEntry> _entries = [];
    private const int MaxEntries = 20;

    public void Capture(string address, string code)
    {
        _entries.Enqueue(new(address, code, DateTimeOffset.UtcNow));

        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);
    }

    public IReadOnlyList<InboxEntry> GetRecent() => [.. _entries.OrderByDescending(entry => entry.SentAt)];
}
