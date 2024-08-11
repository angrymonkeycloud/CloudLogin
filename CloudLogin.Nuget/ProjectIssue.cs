namespace CloudLogin.Nuget;

internal class ProjectIssue(Project project)
{
    readonly public Project Project = project;
    internal bool UpdateMetadataIssue { get; set; } = false;
    internal bool RebuildIssue { get; set; } = false;
    internal bool PackingIssue { get; set; } = false;
    internal bool PublishingIssue { get; set; } = false;
}
