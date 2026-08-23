namespace AngryMonkey.CloudLogin.Models;

public class CloudWorkspaceMemberProfileModel
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
    public string? EmailAddress { get; set; }
    public string? ProfilePicture { get; set; }
    public List<string> Roles { get; set; } = [];
    public bool IsOwner { get; set; }
    public CloudWorkspaceMembershipStates State { get; set; } = CloudWorkspaceMembershipStates.Active;
    public DateTimeOffset JoinedOn { get; set; }

    /// <summary>The best available label for this member, falling back to a shortened id.</summary>
    public string Label => !string.IsNullOrWhiteSpace(DisplayName)
        ? DisplayName!
        : !string.IsNullOrWhiteSpace(EmailAddress)
            ? EmailAddress!
            : $"Member {UserId.ToString()[..8]}";
}

public static class CloudWorkspaceMemberProfileModelExtensions
{
    public static CloudWorkspaceMemberProfileModel ToModel(this CloudWorkspaceMemberProfile source) => new()
    {
        UserId = source.UserId,
        DisplayName = source.DisplayName,
        EmailAddress = source.EmailAddress,
        ProfilePicture = source.ProfilePicture,
        Roles = [.. source.Roles],
        IsOwner = source.IsOwner,
        State = source.State,
        JoinedOn = source.JoinedOn
    };
}
