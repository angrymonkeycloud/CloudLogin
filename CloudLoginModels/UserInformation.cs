namespace AngryMonkey.CloudLogin.Models;

public class UserModel
{
	public string? FirstName { get; set; }
	public string? LastName { get; set; }
	public string DisplayName { get; set; }
	public bool IsLocked { get; set; }
}