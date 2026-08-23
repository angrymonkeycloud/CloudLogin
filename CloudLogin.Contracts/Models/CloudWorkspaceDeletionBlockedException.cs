namespace AngryMonkey.CloudLogin;

/// <summary>Thrown when a workspace still holds subscriptions that prevent deletion.</summary>
public sealed class CloudWorkspaceDeletionBlockedException(CloudWorkspaceDeletionReport report, string singularLabel = "workspace")
    : InvalidOperationException(report.Reasons.Count > 0
        ? string.Join(" ", report.Reasons)
        : $"This {singularLabel} can't be deleted yet.")
{
    public CloudWorkspaceDeletionReport Report { get; } = report;
}
