namespace AngryMonkey.CloudLogin.Server;

public record DataUser : BaseRecord
{
    public DataUser() : base("User", "User") { }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool IsLocked { get; set; } = false;
    public string? Username { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;
    public List<LoginInput> Inputs { get; set; } = new List<LoginInput>();
}