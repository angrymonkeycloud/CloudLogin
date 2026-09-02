using AngryMonkey.CloudLogin.Server.Core;
using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// Rotating the security stamp signs every other device out. That is right when the set of
/// credentials that can prove the person changed, and wrong for a write that changed nothing of
/// the sort - which is how starting an authenticator enrollment (an unconfirmed secret) signed
/// people out everywhere for scanning a QR code.
/// </summary>
public class CoreSecurityStoreTests
{
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryCredentialRepository _credentials = new();
    private readonly CoreSecurityStore _store;

    public CoreSecurityStoreTests()
    {
        CloudLoginCoreConfiguration configuration = new();

        _store = new CoreSecurityStore(
            _credentials,
            new InMemoryAuditEventRepository(),
            _users,
            new EphemeralDataProtectionProvider(),
            configuration);
    }

    [Fact]
    public async Task Starting_an_authenticator_enrollment_does_not_rotate_the_stamp()
    {
        Guid userId = await NewUserAsync();

        await _store.UpdateCredentials(userId, document => document.Authenticator = new CloudLoginAuthenticatorApp
        {
            SecretKey = "JBSWY3DPEHPK3PXP",
            EnrolledOn = DateTimeOffset.UtcNow,
            IsConfirmed = false
        });

        Assert.Equal("stamp-1", await StampAsync(userId));

        // The unconfirmed enrollment is stored all the same, so it can be confirmed.
        Assert.False((await _store.GetCredentials(userId)).Authenticator!.IsConfirmed);
    }

    [Fact]
    public async Task Confirming_the_authenticator_rotates_the_stamp()
    {
        Guid userId = await NewUserAsync();
        await _store.UpdateCredentials(userId, document => document.Authenticator = new CloudLoginAuthenticatorApp
        {
            SecretKey = "JBSWY3DPEHPK3PXP",
            EnrolledOn = DateTimeOffset.UtcNow,
            IsConfirmed = false
        });

        await _store.UpdateCredentials(userId, document => document.Authenticator!.IsConfirmed = true);

        string confirmed = await StampAsync(userId);
        Assert.NotEqual("stamp-1", confirmed);

        // Disabling it is a change of the same kind.
        await _store.UpdateCredentials(userId, document => document.Authenticator = null);
        Assert.NotEqual(confirmed, await StampAsync(userId));
    }

    [Fact]
    public async Task Adding_or_removing_a_passkey_rotates_but_renaming_one_does_not()
    {
        Guid userId = await NewUserAsync();

        await _store.UpdateCredentials(userId, document => document.Passkeys.Add(new CloudLoginPasskey
        {
            CredentialId = "credential-1",
            PublicKey = [1, 2, 3],
            Name = "Laptop",
            CreatedOn = DateTimeOffset.UtcNow
        }));
        string added = await StampAsync(userId);
        Assert.NotEqual("stamp-1", added);

        await _store.UpdateCredentials(userId, document => document.Passkeys[0].Name = "Work laptop");
        Assert.Equal(added, await StampAsync(userId));
        Assert.Equal("Work laptop", (await _store.GetCredentials(userId)).Passkeys[0].Name);

        await _store.UpdateCredentials(userId, document => document.Passkeys.Clear());
        Assert.NotEqual(added, await StampAsync(userId));
    }

    private async Task<Guid> NewUserAsync()
    {
        Guid userId = Guid.NewGuid();
        await _users.CreateAsync(new UserDocument { Id = userId.ToString(), SecurityStamp = "stamp-1" });
        return userId;
    }

    private async Task<string> StampAsync(Guid userId) => (await _users.GetAsync(userId))!.SecurityStamp;
}
