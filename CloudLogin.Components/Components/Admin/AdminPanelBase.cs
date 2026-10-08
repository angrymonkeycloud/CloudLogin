using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.V3;
using Microsoft.AspNetCore.Components;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// Shared behaviour for the admin console's panels: the API client, a busy flag, and one place where a
/// refused call becomes a message instead of a crash.
/// </summary>
public abstract class AdminPanelBase : ComponentBase
{
    [Inject] protected ICloudLogin CloudLogin { get; set; } = default!;

    /// <summary>What the signed-in administrator may do. Panels only offer the actions that will be accepted.</summary>
    [Parameter] public V3AdminMeResponse? Access { get; set; }

    protected CloudLoginAdminClient? Api => (CloudLogin as CloudLoginClient)?.Admin;

    protected bool Busy { get; private set; }
    protected string? Error { get; set; }
    protected string? Notice { get; set; }

    protected bool Can(string permission) => Access?.Permissions.Contains(permission) == true;

    protected async Task RunAsync(Func<Task> action, string? done = null)
    {
        Busy = true;
        Error = null;
        Notice = null;
        StateHasChanged();

        try
        {
            await action();

            if (done is not null)
                Notice = done;
        }
        catch (CloudLoginAdminException exception)
        {
            Error = exception.Message;
        }
        catch (HttpRequestException)
        {
            Error = "The authority could not be reached.";
        }
        finally
        {
            Busy = false;
        }
    }

    protected static string When(DateTimeOffset? value) => value is { } on ? on.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "Never";

    protected static string Ago(DateTimeOffset? value)
    {
        if (value is not { } on)
            return "Never";

        TimeSpan span = DateTimeOffset.UtcNow - on;

        return span.TotalMinutes < 1 ? "Just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
            : span.TotalDays < 60 ? $"{(int)span.TotalDays} d ago"
            : on.ToLocalTime().ToString("yyyy-MM-dd");
    }

    protected static string StatusClass(string status) => status.ToLowerInvariant() switch
    {
        "active" or "success" => "ok",
        "retired" or "expired" or "disabled" or "denied" => "warn",
        "revoked" or "failure" or "locked" => "bad",
        _ => "neutral"
    };
}
