using System.Collections.Concurrent;

namespace CloudLogin.Demo;

/// <summary>
/// Stands in for a real mailbox: the Code provider hands verification codes to this
/// service instead of sending email, and <c>/demo/inbox</c> exposes them so a tester can
/// complete the code-based sign-in flow without SMTP.
/// </summary>
public sealed class DemoInboxService
{
    public sealed record InboxEntry(string Address, string Code, DateTimeOffset SentAt);

    private readonly ConcurrentQueue<InboxEntry> _entries = new();
    private const int MaxEntries = 20;

    public void Capture(string address, string code)
    {
        _entries.Enqueue(new InboxEntry(address, code, DateTimeOffset.UtcNow));

        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);
    }

    public IReadOnlyList<InboxEntry> GetRecent() =>
        [.. _entries.OrderByDescending(entry => entry.SentAt)];
}
