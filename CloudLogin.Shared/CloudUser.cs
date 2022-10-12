using Newtonsoft.Json;

namespace AngryMonkey.Cloud.Login.DataContract;

public record CloudUser : BaseRecord
{
	public CloudUser() : base("CloudUser", "CloudUser") { }

	public string FirstName { get; set; }
	public string LastName { get; set; }
	public bool IsAuthorized { get; set; }
	public string? DisplayName { get; set; }
	public bool IsLocked { get; set; } = false;
	public string? Username { get; set; }
	public DateOnly? DateOfBirth { get; set; }
	public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.MinValue;
	public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;

	// Lists

	public List<LoginInput> Inputs { get; set; } = new List<LoginInput>();

	[JsonIgnore]
	public List<LoginInput> EmailAddresses => Inputs.Where(key => key.Format == InputFormat.EmailAddress).ToList();

	[JsonIgnore]
	public List<LoginInput> PhoneNumbers => Inputs.Where(key => key.Format == InputFormat.PhoneNumber).ToList();


	// Ignore

	[JsonIgnore]
	public LoginInput? PrimaryEmailAddress => EmailAddresses?.FirstOrDefault(key => key.IsPrimary);

	[JsonIgnore]
	public LoginInput? PrimaryPhoneNumber => PhoneNumbers.FirstOrDefault(key => key.IsPrimary);

	[JsonIgnore]
	public List<string> Providers => Inputs.SelectMany(input => input.Providers).Select(key => key.Code).Distinct().ToList();
}