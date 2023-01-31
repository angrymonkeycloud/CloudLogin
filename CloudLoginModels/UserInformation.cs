namespace AngryMonkey.Cloud.Login.Models;
public class UserInformation
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool IsLocked { get; set; } = false;
    public DateOnly? DateOfBirth { get; set; }
}