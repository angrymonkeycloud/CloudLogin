using System.Security.Claims;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// The properties that make a six-digit code safe to sign in with. Every one of them is a property
/// of the server: the browser is handed a challenge handle and nothing else, so none of this can be
/// satisfied by a well-behaved client or defeated by a badly behaved one.
/// </summary>
public class CloudLoginVerificationCodeTests
{
    [Fact]
    public async Task ACorrectCode_SignsTheAccountIn()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture);
        CloudLoginVerificationResult result = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, IssuedCode(fixture), keepMeSignedIn: true));

        Assert.Equal(CloudLoginVerificationStatuses.Verified, result.Status);
        Assert.Equal(1, fixture.Authentication.SignInCount);
        Assert.Equal(
            user.Id.ToString(),
            fixture.Authentication.SignedInPrincipal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(fixture.Authentication.SignedInProperties!.IsPersistent);
    }

    [Fact]
    public async Task TheCodeNeverReachesTheCaller()
    {
        // The whole point of the change: what comes back is a handle, and the handle is not the code.
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture);

        Assert.DoesNotContain(IssuedCode(fixture), challenge.ChallengeId, StringComparison.Ordinal);
        Assert.True(challenge.ExpiresOn > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AWrongCode_DoesNotSignIn()
    {
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture);
        CloudLoginVerificationResult result = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, WrongCode(fixture)));

        Assert.Equal(CloudLoginVerificationStatuses.Invalid, result.Status);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task ACorrectCode_IsSpentByTheSignInItCompletes()
    {
        // Replaying the same code must buy nothing, however many times it is offered.
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture);
        string code = IssuedCode(fixture);

        await fixture.Server.VerifyCode(CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, code));
        CloudLoginVerificationResult replay = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, code));

        Assert.Equal(CloudLoginVerificationStatuses.NotFound, replay.Status);
        Assert.Equal(1, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task GuessingIsCapped_AndTheChallengeDiesWithTheLastAttempt()
    {
        // Six digits are only safe because a challenge tolerates a handful of wrong answers. Without
        // this, a million guesses is an afternoon's work.
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();
        fixture.Configuration.Security.MaximumVerificationAttempts = 3;

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture);
        string wrong = WrongCode(fixture);

        Assert.Equal(
            CloudLoginVerificationStatuses.Invalid,
            (await fixture.Server.VerifyCode(CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, wrong))).Status);
        Assert.Equal(
            CloudLoginVerificationStatuses.Invalid,
            (await fixture.Server.VerifyCode(CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, wrong))).Status);
        Assert.Equal(
            CloudLoginVerificationStatuses.TooManyAttempts,
            (await fixture.Server.VerifyCode(CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, wrong))).Status);

        // Even the correct code is worthless once the challenge is dead.
        CloudLoginVerificationResult afterDeath = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, IssuedCode(fixture)));

        Assert.Equal(CloudLoginVerificationStatuses.NotFound, afterDeath.Status);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task AnExpiredCode_IsRefused()
    {
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();
        fixture.Configuration.Security.VerificationCodeLifetime = TimeSpan.FromMilliseconds(1);

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture);
        await Task.Delay(20);

        CloudLoginVerificationResult result = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, IssuedCode(fixture)));

        Assert.Equal(CloudLoginVerificationStatuses.Expired, result.Status);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task AnInventedChallenge_IsRefused()
    {
        // A caller that never asked for a code has nothing to redeem, whatever it sends.
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync();

        CloudLoginVerificationResult result = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create("not-a-challenge", "000000"));

        Assert.Equal(CloudLoginVerificationStatuses.NotFound, result.Status);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task ACodeForAnAddressWithNoAccount_VerifiesWithoutSigningAnyoneIn()
    {
        LoginTestFixture fixture = new();

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture, "newcomer@example.com");
        CloudLoginVerificationResult result = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, IssuedCode(fixture)));

        Assert.Equal(CloudLoginVerificationStatuses.NoAccount, result.Status);
        Assert.Equal(0, fixture.Authentication.SignInCount);
        Assert.False(string.IsNullOrWhiteSpace(result.VerificationToken));
    }

    [Fact]
    public async Task RegistrationRequiresTheProof_AndSpendsIt()
    {
        LoginTestFixture fixture = new();

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture, "newcomer@example.com");
        CloudLoginVerificationResult verified = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, IssuedCode(fixture)));

        CloudUser user = await fixture.Server.CodeRegistration(CloudLoginCodeRegistrationRequest.Create(
            "newcomer@example.com",
            CloudLoginInputFormat.EmailAddress,
            "New",
            "Comer",
            verificationToken: verified.VerificationToken));

        Assert.Equal("newcomer@example.com", user.Inputs[0].Input);
        Assert.Equal(1, fixture.Authentication.SignInCount);

        // The same proof cannot create a second account.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Server.CodeRegistration(CloudLoginCodeRegistrationRequest.Create(
                "someone.else@example.com",
                CloudLoginInputFormat.EmailAddress,
                "Someone",
                "Else",
                verificationToken: verified.VerificationToken)));
    }

    [Fact]
    public async Task RegistrationWithoutProof_IsRefused()
    {
        // The old flow created an account for whatever address the browser named. This is the test
        // that says it cannot any more.
        LoginTestFixture fixture = new();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Server.CodeRegistration(CloudLoginCodeRegistrationRequest.Create(
                "victim@example.com",
                CloudLoginInputFormat.EmailAddress,
                "Not",
                "Verified")));
    }

    [Fact]
    public async Task AProofForOneAddress_CannotRegisterAnother()
    {
        LoginTestFixture fixture = new();

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture, "mine@example.com");
        CloudLoginVerificationResult verified = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, IssuedCode(fixture)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Server.CodeRegistration(CloudLoginCodeRegistrationRequest.Create(
                "victim@example.com",
                CloudLoginInputFormat.EmailAddress,
                "Not",
                "Mine",
                verificationToken: verified.VerificationToken)));
    }

    [Fact]
    public async Task ATestAccount_CannotBeReachedByCode()
    {
        // Test accounts sign in through the test-mode endpoint alone, exactly as for a password.
        LoginTestFixture fixture = new();
        await fixture.AddPasswordUserAsync("tester@example.com", isTest: true);

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture, "tester@example.com");
        CloudLoginVerificationResult result = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, IssuedCode(fixture)));

        Assert.Equal(CloudLoginVerificationStatuses.NoAccount, result.Status);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task ALockedAccount_CannotBeReachedByCode()
    {
        LoginTestFixture fixture = new();
        CloudUser user = await fixture.AddPasswordUserAsync();
        user.IsLocked = true;

        CloudLoginVerificationChallenge challenge = await SendAsync(fixture);
        CloudLoginVerificationResult result = await fixture.Server.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(challenge.ChallengeId, IssuedCode(fixture)));

        Assert.Equal(CloudLoginVerificationStatuses.NoAccount, result.Status);
        Assert.Equal(0, fixture.Authentication.SignInCount);
    }

    [Fact]
    public async Task TheCodeIsAddressedAndShaped_AsConfigured()
    {
        LoginTestFixture fixture = new();
        fixture.Configuration.Security.VerificationCodeLength = 8;

        await SendAsync(fixture, "  Person@Example.COM ");

        CloudLoginSendCodeValue sent = Assert.Single(fixture.SentCodes);

        Assert.Equal("person@example.com", sent.Address);
        Assert.Equal(8, sent.Code.Length);
        Assert.All(sent.Code, character => Assert.True(char.IsAsciiDigit(character)));
    }

    private static Task<CloudLoginVerificationChallenge> SendAsync(
        LoginTestFixture fixture,
        string address = "person@example.com") =>
        fixture.Server.SendVerificationCode(
            CloudLoginSendCodeRequest.Create(address, CloudLoginVerificationPurposes.SignIn));

    private static string IssuedCode(LoginTestFixture fixture) => fixture.SentCodes[^1].Code;

    private static string WrongCode(LoginTestFixture fixture)
    {
        string code = IssuedCode(fixture);

        return code[0] == '0' ? $"1{code[1..]}" : $"0{code[1..]}";
    }
}
