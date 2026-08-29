namespace CloudLogin.Demo;

public static class DemoCodeSamples
{
    public const string Registration = """
builder.Services.AddCloudLoginAccountRegistry();

// Replace the in-memory store with your private persistence adapter.
builder.Services.AddSingleton<ICloudLoginAccountStore, ApplicationAccountStore>();
""";

    public const string Workspaces = """
CloudWorkspace workspace = await workspaces.CreateAsync("Cedar Labs", ownerUserId);
CloudWorkspaceMember member = await workspaces.AddMemberAsync(
    workspace.Id, userId, ["BillingAdmin", "Developer"]);
CloudWorkspaceInvitation invitation = await workspaces.InviteAsync(
    workspace.Id, "developer@example.com", ownerUserId,
    DateTimeOffset.UtcNow.AddDays(7), ["Developer"]);
""";

}
