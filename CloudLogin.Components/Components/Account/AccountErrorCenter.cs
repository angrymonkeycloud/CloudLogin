namespace AngryMonkey.CloudLogin;

/// <summary>
/// One recorded failure from anywhere in the account page, kept for display in the
/// error dialog. Deliberately separate from the inline "alert-error" text each tab already
/// shows next to the control that failed — this is the account-wide record of it, so a
/// failure is still visible to the user even after they've navigated to a different tab.
/// </summary>
internal sealed record AccountErrorEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Where this happened, e.g. "Security — Passkeys" or "Profile — Email addresses".</summary>
    public required string Source { get; init; }

    /// <summary>The same user-facing message already shown inline at the point of failure.</summary>
    public required string Message { get; init; }

    /// <summary>Full exception text, when one is available, for the "Show details" disclosure.</summary>
    public string? Details { get; init; }
}

/// <summary>
/// Collects failures from every tab of the account page into one place, so the navigation
/// rail can surface "something went wrong" even when the user isn't looking at the tab where
/// it happened. One instance lives for the lifetime of a single <c>AccountComponent</c> and is
/// handed down to every tab via <see cref="Microsoft.AspNetCore.Components.CascadingValue{TValue}"/> —
/// it does not persist across page loads or leak between users.
/// </summary>
internal sealed class AccountErrorCenter
{
    private const int MaximumEntries = 50;

    private readonly List<AccountErrorEntry> _entries = [];

    public IReadOnlyList<AccountErrorEntry> Entries => _entries;

    /// <summary>Raised after the entry list changes, so the nav badge can repaint.</summary>
    public event Action? Changed;

    public void Report(string source, string message, Exception? exception = null)
    {
        _entries.Insert(0, new AccountErrorEntry
        {
            Source = source,
            Message = message,
            Details = exception?.ToString()
        });

        if (_entries.Count > MaximumEntries)
            _entries.RemoveRange(MaximumEntries, _entries.Count - MaximumEntries);

        Changed?.Invoke();
    }

    public void Dismiss(Guid id)
    {
        _entries.RemoveAll(entry => entry.Id == id);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_entries.Count == 0)
            return;

        _entries.Clear();
        Changed?.Invoke();
    }
}
