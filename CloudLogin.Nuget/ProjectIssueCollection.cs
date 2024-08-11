namespace CloudLogin.Nuget;

internal class ProjectIssueCollection
{
    readonly public List<ProjectIssue> Projects = [];

    internal void AddMetadataIssue(Project project)
    {
        ProjectIssue p = GetProject(project);

        p.UpdateMetadataIssue = true;
    }

    internal void AddRebuildIssue(Project project)
    {
        ProjectIssue p = GetProject(project);

        p.RebuildIssue = true;
    }

    internal void AddPackingIssue(Project project)
    {
        ProjectIssue p = GetProject(project);

        p.PackingIssue = true;
    }

    internal void AddPublishingIssue(Project project)
    {
        ProjectIssue p = GetProject(project);

        p.PublishingIssue = true;
    }

    private ProjectIssue GetProject(Project project)
    {
        ProjectIssue? p = Projects.FirstOrDefault(key => key.Project.Name.Equals(project.Name));

        return p ??= new(project);
    }

    internal bool HasIssues => Projects.Count > 0;

    internal void LogIssues()
    {
        foreach (ProjectIssue projectIssue in Projects)
        {
            List<string> issues = [];

            if (projectIssue.UpdateMetadataIssue)
                issues.Add("Update Metadata Issue");

            if (projectIssue.RebuildIssue)
                issues.Add("Rebuild Issue");

            if (projectIssue.PackingIssue)
                issues.Add("Packing Issue");

            if (projectIssue.PublishingIssue)
                issues.Add("Publishing Issue");

            Console.WriteLine($"{projectIssue.Project.Name} | {string.Join(", ", issues)}");
        }
    }
}