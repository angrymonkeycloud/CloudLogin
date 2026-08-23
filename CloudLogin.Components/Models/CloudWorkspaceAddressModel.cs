namespace AngryMonkey.CloudLogin.Models;

public class CloudWorkspaceAddressModel
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Line1)
        && string.IsNullOrWhiteSpace(Line2)
        && string.IsNullOrWhiteSpace(City)
        && string.IsNullOrWhiteSpace(State)
        && string.IsNullOrWhiteSpace(PostalCode)
        && string.IsNullOrWhiteSpace(Country);
}

public static class CloudWorkspaceAddressModelExtensions
{
    public static CloudWorkspaceAddressModel ToModel(this CloudWorkspaceAddress source) => new()
    {
        Line1 = source.Line1,
        Line2 = source.Line2,
        City = source.City,
        State = source.State,
        PostalCode = source.PostalCode,
        Country = source.Country
    };

    public static CloudWorkspaceAddress ToContract(this CloudWorkspaceAddressModel model) => new()
    {
        Line1 = model.Line1,
        Line2 = model.Line2,
        City = model.City,
        State = model.State,
        PostalCode = model.PostalCode,
        Country = model.Country
    };
}
