namespace AngryMonkey.CloudLogin;

public sealed record CloudLoginInviteToWorkspaceRequest(string Recipient, IReadOnlyList<string>? Roles = null);
