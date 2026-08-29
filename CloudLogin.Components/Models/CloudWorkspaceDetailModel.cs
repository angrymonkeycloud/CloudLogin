namespace AngryMonkey.CloudLogin.Models;

public class CloudWorkspaceDetailModel
{
    public CloudWorkspaceModel Workspace { get; set; } = new();
    public bool IsOwner { get; set; }
    public bool CanManage { get; set; }
    public List<string> Roles { get; set; } = [];
    public List<CloudWorkspaceMemberProfileModel> Members { get; set; } = [];
    public CloudWorkspaceDeletionReport? Deletion { get; set; }
}

public static class CloudWorkspaceDetailModelExtensions
{
    public static CloudWorkspaceDetailModel ToModel(this CloudWorkspaceDetail source) => new()
    {
        Workspace = source.Workspace.ToModel(),
        IsOwner = source.IsOwner,
        CanManage = source.CanManage,
        Roles = [.. source.Roles],
        Members = [.. source.Members.Select(member => member.ToModel())],
        Deletion = source.Deletion
    };
}
