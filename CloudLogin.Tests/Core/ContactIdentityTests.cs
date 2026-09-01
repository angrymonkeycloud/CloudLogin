using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// Contacts carry an immutable id, and everything that points at a contact points at that id
/// rather than at the address. These tests pin the consequences: a corrected address keeps its
/// password, and the stored credential never contains the address at all.
/// </summary>
public class ContactIdentityTests
{
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryCredentialRepository _credentials = new();
    private readonly InMemoryIdentityKeyStore _identityKeys = new(TestIdentityHmac.Hasher);
    private readonly InMemoryAuditEventRepository _audit = new();
    private readonly CloudLoginCoreConfiguration _configuration = new();
    private readonly CoreUserService _service;

    public ContactIdentityTests()
    {
        IdentityNormalization normalization = new(new CloudGeographyClient());
        IdentityLinkingService linking = new(
            _identityKeys, _credentials, _users, _configuration, new AuditLogger(_audit, _configuration));

        _service = new CoreUserService(_users, _credentials, linking, normalization);
    }

    private static CloudUser BuildUser(string email = "ada@example.com", string passwordHash = "PBKDF2$hash") => new()
    {
        ID = Guid.NewGuid(),
        FirstName = "Ada",
        LastName = "Lovelace",
        DisplayName = "Ada Lovelace",
        CreatedOn = DateTimeOffset.UtcNow,
        Inputs =
        [
            new CloudLoginInput
            {
                Input = email,
                Format = CloudLoginInputFormat.EmailAddress,
                IsPrimary = true,
                Providers = [new CloudLoginProvider { Code = "Password", PasswordHash = passwordHash }]
            }
        ]
    };

    // ── Contact ids ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryContact_GetsAnId()
    {
        CloudUser user = BuildUser();
        await _service.SaveAsync(user, isCreate: true);

        UserContact contact = Assert.Single((await _users.GetAsync(user.ID))!.Contacts);
        Assert.NotEqual(Guid.Empty, contact.ContactId);
    }

    [Fact]
    public async Task ContactId_SurvivesAProfileSave()
    {
        CloudUser user = BuildUser();
        await _service.SaveAsync(user, isCreate: true);
        Guid original = (await _users.GetAsync(user.ID))!.Contacts[0].ContactId;

        CloudUser reloaded = (await _service.LoadAsync(user.ID))!;
        reloaded.FirstName = "Augusta";
        await _service.SaveAsync(reloaded, isCreate: false);

        Assert.Equal(original, (await _users.GetAsync(user.ID))!.Contacts[0].ContactId);
    }

    [Fact]
    public async Task ContactId_SurvivesRecasingTheAddress()
    {
        // Normalization makes these the same contact, so the id must not move — the credential
        // document is named after it.
        CloudUser user = BuildUser("ada@example.com");
        await _service.SaveAsync(user, isCreate: true);
        Guid original = (await _users.GetAsync(user.ID))!.Contacts[0].ContactId;

        CloudUser reloaded = (await _service.LoadAsync(user.ID))!;
        reloaded.Inputs[0].Input = "Ada@Example.COM";
        await _service.SaveAsync(reloaded, isCreate: false);

        Assert.Equal(original, (await _users.GetAsync(user.ID))!.Contacts[0].ContactId);

        // And the password still resolves through that contact.
        Assert.Equal("PBKDF2$hash",
            (await _service.LoadAsync(user.ID))!.Inputs[0].Providers
                .Single(provider => provider.Code == "Password").PasswordHash);
    }

    [Fact]
    public async Task DifferentContacts_GetDifferentIds()
    {
        CloudUser user = BuildUser();
        user.Inputs.Add(new CloudLoginInput
        {
            Input = "second@example.com",
            Format = CloudLoginInputFormat.EmailAddress
        });

        await _service.SaveAsync(user, isCreate: true);

        List<UserContact> contacts = (await _users.GetAsync(user.ID))!.Contacts;
        Assert.Equal(2, contacts.Count);
        Assert.NotEqual(contacts[0].ContactId, contacts[1].ContactId);
    }

    // ── Credentials reference the contact, never the address ──────────────────

    [Fact]
    public async Task PasswordCredential_IsNamedAfterTheContactId()
    {
        CloudUser user = BuildUser();
        await _service.SaveAsync(user, isCreate: true);

        Guid contactId = (await _users.GetAsync(user.ID))!.Contacts[0].ContactId;
        CredentialDocument password = Assert.Single(
            await _credentials.GetAllForUserAsync(user.ID),
            credential => credential.Kind == CredentialKinds.Password);

        Assert.Equal($"password|{contactId}", password.Id);
        Assert.Equal(contactId, password.ContactId);
        Assert.Equal(user.ID.ToString(), password.UserId);
    }

    [Fact]
    public async Task CredentialDocuments_NeverCarryAnEmailOrPhoneAsAKey()
    {
        CloudUser user = BuildUser("ada@example.com");
        user.Inputs[0].Providers.Add(new CloudLoginProvider { Code = "Google", Identifier = "google-sub-1" });

        await _service.SaveAsync(user, isCreate: true);

        foreach (CredentialDocument credential in await _credentials.GetAllForUserAsync(user.ID))
        {
            // The id and the reference columns are the keys. ProviderEmail is display-only and is
            // allowed to hold the address; nothing routes on it.
            Assert.DoesNotContain("ada@example.com", credential.Id, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(credential.UserId);
        }

        Assert.DoesNotContain(typeof(CredentialDocument).GetProperties(),
            property => property.Name is "ContactKey" or "LinkedContactKey");
    }

    [Fact]
    public async Task ExternalIdentity_RecordsProviderEmailAndItsVerifiedStatus()
    {
        CloudUser user = BuildUser();
        user.Inputs[0].Providers.Add(new CloudLoginProvider { Code = "Google", Identifier = "google-sub-1" });

        await _service.SaveAsync(user, isCreate: true);

        Guid contactId = (await _users.GetAsync(user.ID))!.Contacts[0].ContactId;
        CredentialDocument external = Assert.Single(
            await _credentials.GetAllForUserAsync(user.ID),
            credential => credential.Kind == CredentialKinds.ExternalIdentity);

        Assert.Equal(contactId, external.LinkedContactId);
        Assert.Equal("ada@example.com", external.ProviderEmail);
        Assert.True(external.ProviderEmailIsVerified);
        Assert.Equal("google-sub-1", external.Subject);
    }

    // ── The identity index knows which contact it belongs to ──────────────────

    [Fact]
    public async Task IdentityKey_PointsAtTheContactItReserves()
    {
        CloudUser user = BuildUser();
        await _service.SaveAsync(user, isCreate: true);

        Guid contactId = (await _users.GetAsync(user.ID))!.Contacts[0].ContactId;
        IdentityKey key = (await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")))!;

        Assert.Equal(user.ID, key.UserId);
        Assert.Equal(contactId, key.ContactId);
    }

    [Fact]
    public async Task RemovingAContact_ReleasesItsIdentityAndItsPassword()
    {
        CloudUser user = BuildUser();
        user.Inputs.Add(new CloudLoginInput
        {
            Input = "second@example.com",
            Format = CloudLoginInputFormat.EmailAddress,
            Providers = [new CloudLoginProvider { Code = "Password", PasswordHash = "PBKDF2$second" }]
        });
        await _service.SaveAsync(user, isCreate: true);

        Guid removedContactId = (await _users.GetAsync(user.ID))!.Contacts
            .Single(contact => contact.NormalizedValue == "second@example.com").ContactId;

        CloudUser reloaded = (await _service.LoadAsync(user.ID))!;
        reloaded.Inputs.RemoveAll(input => input.Input == "second@example.com");
        await _service.SaveAsync(reloaded, isCreate: false);

        Assert.Null(await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("second@example.com")));
        Assert.DoesNotContain(await _credentials.GetAllForUserAsync(user.ID),
            credential => credential.ContactId == removedContactId);

        // The contact that stayed is untouched.
        Assert.NotNull(await _identityKeys.ResolveAsync("default", IdentityKey.CanonicalEmail("ada@example.com")));
    }
}
