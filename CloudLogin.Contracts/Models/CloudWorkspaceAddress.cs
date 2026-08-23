namespace AngryMonkey.CloudLogin;

public sealed class CloudWorkspaceAddress
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

    /// <summary>The populated parts, in postal order, joined for display on a single line.</summary>
    public override string ToString() => string.Join(", ", new[] { Line1, Line2, City, State, PostalCode, Country }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
