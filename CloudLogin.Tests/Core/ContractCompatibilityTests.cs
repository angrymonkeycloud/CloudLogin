using System.Reflection;
using System.Text.Json;
using AngryMonkey.CloudLogin.V3;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// V2 contract snapshots: the JSON a V2 integration receives must not change shape while the
/// storage underneath is replaced. These tests pin the serialized property set.
/// </summary>
public class V2ContractSnapshotTests
{
    private static readonly string[] ExpectedCloudUserProperties =
    [
        "ID", "FirstName", "LastName", "DisplayName", "IsLocked", "IsTest", "IsGlobalAdmin",
        "Username", "DateOfBirth", "CreatedOn", "LastSignedIn", "Inputs",
        "ProfilePicture", "IsCustomProfilePicture", "ProviderProfilePicture", "Country", "Locale"
    ];

    private static readonly string[] ExpectedInputProperties =
    [
        "Format", "Input", "IsPrimary", "PhoneNumberCountryCode", "PhoneNumberCallingCode", "Providers"
    ];

    private static readonly string[] ExpectedProviderProperties = ["Code", "PasswordHash", "Identifier"];

    // Every property populated: WhenWritingNull omits nulls, so a fully populated sample pins
    // the complete wire shape.
    private static CloudUser SampleUser() => new()
    {
        ID = Guid.Parse("b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11"),
        FirstName = "Ada",
        LastName = "Lovelace",
        DisplayName = "Ada Lovelace",
        Username = "ada",
        DateOfBirth = new DateOnly(1815, 12, 10),
        CreatedOn = new DateTimeOffset(2026, 1, 14, 9, 12, 0, TimeSpan.Zero),
        LastSignedIn = new DateTimeOffset(2026, 8, 20, 17, 3, 44, TimeSpan.Zero),
        ProfilePicture = "https://cdn.example/ada.png",
        IsCustomProfilePicture = true,
        ProviderProfilePicture = "https://lh3.googleusercontent.com/a/AC",
        Country = "GB",
        Locale = "en-GB",
        Inputs =
        [
            new CloudLoginInput
            {
                Input = "70123456",
                Format = CloudLoginInputFormat.PhoneNumber,
                IsPrimary = true,
                PhoneNumberCountryCode = "LB",
                PhoneNumberCallingCode = "961",
                Providers =
                [
                    new CloudLoginProvider { Code = "Password", PasswordHash = "AQAAAA-not-for-transport" },
                    new CloudLoginProvider { Code = "Google", Identifier = "104839571023984710" }
                ]
            }
        ]
    };

    [Fact]
    public void CloudUser_SerializedShape_IsStable()
    {
        string json = JsonSerializer.Serialize(SampleUser(), CloudLoginSerialization.Options);
        using JsonDocument document = JsonDocument.Parse(json);

        string[] actual = [.. document.RootElement.EnumerateObject().Select(property => property.Name)];

        Assert.Equal(ExpectedCloudUserProperties.OrderBy(name => name), actual.OrderBy(name => name));
    }

    [Fact]
    public void CloudLoginInput_SerializedShape_IsStable()
    {
        string json = JsonSerializer.Serialize(SampleUser().Inputs[0], CloudLoginSerialization.Options);
        using JsonDocument document = JsonDocument.Parse(json);

        string[] actual = [.. document.RootElement.EnumerateObject().Select(property => property.Name)];

        Assert.Equal(ExpectedInputProperties.OrderBy(name => name), actual.OrderBy(name => name));
    }

    [Fact]
    public void CloudLoginProvider_SerializedShape_IsStable()
    {
        CloudLoginProvider provider = new() { Code = "Password", PasswordHash = "AQAAAA", Identifier = "104839571023984710" };
        string json = JsonSerializer.Serialize(provider, CloudLoginSerialization.Options);
        using JsonDocument document = JsonDocument.Parse(json);

        string[] actual = [.. document.RootElement.EnumerateObject().Select(property => property.Name)];

        Assert.Equal(ExpectedProviderProperties.OrderBy(name => name), actual.OrderBy(name => name));
    }

    [Fact]
    public void TransportStrippedUser_NeverCarriesAHash()
    {
        CloudUser stripped = CloudLoginTransportSecurity.ForTransport(SampleUser())!;
        string json = JsonSerializer.Serialize(stripped, CloudLoginSerialization.Options);

        Assert.DoesNotContain("AQAAAA-not-for-transport", json);
    }

    [Fact]
    public void TransportStrippedUser_NeverCarriesAProviderSubject()
    {
        // The provider's subject is a credential, not profile data: it is what the identity index
        // is keyed on, and it correlates the same person across services. The V2 wire shape is
        // preserved, but its secret-bearing values are not — no API version restores this one for
        // compatibility.
        CloudUser stripped = CloudLoginTransportSecurity.ForTransport(SampleUser())!;
        string json = JsonSerializer.Serialize(stripped, CloudLoginSerialization.Options);

        Assert.DoesNotContain("104839571023984710", json);
        Assert.All(stripped.Inputs.SelectMany(input => input.Providers), provider => Assert.Null(provider.Identifier));

        // The shape itself is unchanged — the property is still there, just empty.
        Assert.Equal(
            SampleUser().Inputs.SelectMany(input => input.Providers).Select(provider => provider.Code),
            stripped.Inputs.SelectMany(input => input.Providers).Select(provider => provider.Code));
    }

    [Fact]
    public void AnonymousDiscovery_ExposesOnlyProviderCodes()
    {
        CloudUser discovery = CloudLoginTransportSecurity.ForAnonymousDiscovery(SampleUser())!;
        string json = JsonSerializer.Serialize(discovery, CloudLoginSerialization.Options);

        Assert.DoesNotContain("AQAAAA-not-for-transport", json);
        Assert.DoesNotContain("104839571023984710", json);
        Assert.DoesNotContain("Ada", json);
    }
}

/// <summary>
/// V3 serialization safety: no DTO may expose secret or internal storage material, by property
/// name and by serialized output.
/// </summary>
public class V3SerializationTests
{
    private static readonly string[] ForbiddenNameFragments =
    [
        "PasswordHash", "Password", "Secret", "TokenHash", "Subject", "Identifier",
        "ETag", "PartitionKey", "Ttl", "SchemaVersion", "PrivateKey", "SigningKey"
    ];

    // Responses only: requests carry user input (a login identifier is data the caller typed,
    // not something the server exposed).
    public static IEnumerable<object[]> V3Types() =>
        typeof(V3SelfProfileResponse).Assembly.GetTypes()
            .Where(type => type.Namespace == "AngryMonkey.CloudLogin.V3" && type.IsPublic && !type.IsEnum
                && type.Name.EndsWith("Response", StringComparison.Ordinal))
            .Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(V3Types))]
    public void V3Dtos_ExposeNoSecretOrStorageInternals(Type dtoType)
    {
        foreach (PropertyInfo property in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            foreach (string forbidden in ForbiddenNameFragments)
                Assert.False(
                    property.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"{dtoType.Name}.{property.Name} matches forbidden fragment '{forbidden}'.");
    }

    [Fact]
    public void V3SelfProfile_SerializesWithoutProviderInternals()
    {
        V3SelfProfileResponse response = new()
        {
            UserId = Guid.NewGuid(),
            DisplayName = "Ada",
            Contacts =
            [
                new V3ContactModel { Format = "EmailAddress", Value = "ada@example.com", IsPrimary = true, Providers = ["Password", "Google"] }
            ]
        };

        string json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("Hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Subject", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_etag", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V3DeviceResponses_UseRfc8628FieldNames()
    {
        V3DeviceAuthorizationResponse response = new()
        {
            DeviceCode = "dc",
            UserCode = "ABCD-EFGH",
            VerificationUri = "https://login.example/device",
            VerificationUriComplete = "https://login.example/device?user_code=ABCDEFGH",
            ExpiresIn = 600,
            Interval = 5
        };

        string json = JsonSerializer.Serialize(response);

        Assert.Contains("\"device_code\"", json);
        Assert.Contains("\"user_code\"", json);
        Assert.Contains("\"verification_uri\"", json);
        Assert.Contains("\"verification_uri_complete\"", json);
        Assert.Contains("\"expires_in\"", json);
        Assert.Contains("\"interval\"", json);
    }
}
