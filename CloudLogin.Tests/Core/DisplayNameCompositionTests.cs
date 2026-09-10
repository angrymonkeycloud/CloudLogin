using System.Text.Json;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// The display name is composed from the first and last names, and is not a field of its own.
/// </summary>
/// <remarks>
/// What these pin down is the absence: nothing writes it, nothing reads it back, and no caller
/// can set it. A stored copy is what let a renamed account keep showing its old name on every
/// screen that preferred the stored value over the two it was supposedly built from.
/// </remarks>
public class DisplayNameCompositionTests
{
    [Theory]
    [InlineData("Ada", "Lovelace", "Ada Lovelace")]
    [InlineData("  Ada  ", "  Lovelace  ", "Ada Lovelace")]
    [InlineData("Ada", null, "Ada")]
    [InlineData(null, "Lovelace", "Lovelace")]
    [InlineData("Ada", "   ", "Ada")]
    [InlineData(null, null, null)]
    [InlineData("", "", null)]
    public void Compose_JoinsWhateverIsPresent(string? firstName, string? lastName, string? expected)
        => Assert.Equal(expected, CloudLoginDisplayName.Compose(firstName, lastName));

    /// <summary>
    /// Nothing but the two names, so a caller that only has one of them never renders a stray
    /// separator - the reason this composes rather than interpolating "{first} {last}".
    /// </summary>
    [Fact]
    public void Compose_NeverLeavesADanglingSeparator()
    {
        Assert.Equal("Ada", CloudLoginDisplayName.Compose("Ada", null));
        Assert.Equal("Lovelace", CloudLoginDisplayName.Compose(null, "Lovelace"));
    }

    [Fact]
    public void CloudUser_ComposesFromItsOwnNames()
    {
        CloudUser user = new() { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" };

        Assert.Equal("Ada Lovelace", user.DisplayName);
    }

    /// <summary>Renaming moves the display name with it, because there is no second copy to update.</summary>
    [Fact]
    public void CloudUser_FollowsARename()
    {
        CloudUser user = new() { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" };

        user.LastName = "Byron";

        Assert.Equal("Ada Byron", user.DisplayName);
    }

    /// <summary>
    /// It still leaves the API payload, so a client reading the JSON by hand keeps the field it
    /// always had - it is the write direction that is gone.
    /// </summary>
    [Fact]
    public void CloudUser_StillSerializesTheComposedValue()
    {
        CloudUser user = new() { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lovelace" };

        string json = JsonSerializer.Serialize(user);

        Assert.Contains("\"DisplayName\":\"Ada Lovelace\"", json);
    }

    /// <summary>
    /// A document written before this change still carries a stored display name. Reading one
    /// drops it, and writing the document back does not carry it along - so the column clears
    /// itself on the next save instead of lingering as a value nothing maintains.
    /// </summary>
    [Fact]
    public void UserDocument_DropsAStoredDisplayNameInsteadOfRoundTrippingIt()
    {
        const string legacyJson = """
            {"id":"b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11","FirstName":"Ada","LastName":"Byron","DisplayName":"Ada Lovelace"}
            """;

        Server.Core.Domain.UserDocument? document =
            JsonSerializer.Deserialize<Server.Core.Domain.UserDocument>(legacyJson);

        Assert.NotNull(document);

        string rewritten = JsonSerializer.Serialize(document);

        Assert.DoesNotContain("DisplayName", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ada Lovelace", rewritten);

        // And what the account now shows is composed from the names the document does keep.
        Assert.Equal("Ada Byron", CloudLoginDisplayName.Compose(document!.FirstName, document.LastName));
    }

    /// <summary>
    /// A display name arriving from outside is ignored rather than applied: there is no setter
    /// behind the property, so deserialization has nowhere to put it.
    /// </summary>
    [Fact]
    public void CloudUser_IgnoresADisplayNameSentBackIn()
    {
        const string json = """
            {"Id":"b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11","FirstName":"Ada","LastName":"Lovelace","DisplayName":"Someone Else"}
            """;

        CloudUser? user = CloudUser.Parse(json);

        Assert.NotNull(user);
        Assert.Equal("Ada Lovelace", user!.DisplayName);
    }
}
