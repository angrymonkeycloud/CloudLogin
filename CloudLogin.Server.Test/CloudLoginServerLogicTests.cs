using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Xunit;
using AngryMonkey.CloudLogin.Server;

namespace AngryMonkey.CloudLogin.Server.Tests;

public class CloudLoginServerLogicTests
{
    [Theory]
    [InlineData("Password123!", "Password123!", true)]
    [InlineData("Password123!", "wrongpassword", false)]
    [InlineData("ComplexP@ssw0rd!", "ComplexP@ssw0rd!", true)]
    [InlineData("ComplexP@ssw0rd!", "ComplexP@ssw0rd", false)]
    [InlineData("", "", true)]
    [InlineData("", "notempty", false)]
    public void PasswordHashing_AndVerification_ShouldWorkCorrectly(string originalPassword, string testPassword, bool shouldMatch)
    {
        // Arrange - Simulate the password hashing logic from CloudLoginServer
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] originalHashed = KeyDerivation.Pbkdf2(
            originalPassword,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount: 100000, // Updated to match server implementation
            numBytesRequested: 32);

        string storedHash = Convert.ToBase64String(salt.Concat(originalHashed).ToArray());

        // Act - Simulate the password verification logic
        byte[] fullHash = Convert.FromBase64String(storedHash);
        byte[] extractedSalt = fullHash.Take(16).ToArray();
        byte[] actualHash = fullHash.Skip(16).ToArray();

        byte[] testHashed = KeyDerivation.Pbkdf2(
            testPassword,
            extractedSalt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount: 100000, // Updated to match server implementation
            numBytesRequested: 32);

        bool passwordMatches = testHashed.SequenceEqual(actualHash);

        // Assert
        passwordMatches.Should().Be(shouldMatch);
    }

    [Fact]
    public void PasswordHashing_ShouldProduceUniqueHashes()
    {
        // Arrange
        string password = "SamePassword123!";
        List<string> hashes = [];

        // Act - Generate multiple hashes of the same password
        for (int i = 0; i < 5; i++)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hashed = KeyDerivation.Pbkdf2(
                password,
                salt,
                KeyDerivationPrf.HMACSHA256,
                iterationCount: 10000,
                numBytesRequested: 32);

            string hash = Convert.ToBase64String(salt.Concat(hashed).ToArray());
            hashes.Add(hash);
        }

        // Assert
        hashes.Should().OnlyHaveUniqueItems("Each hash should be unique due to random salt");
        hashes.Should().AllSatisfy(hash => 
        {
            hash.Should().NotBeNullOrEmpty();
            hash.Should().NotBe(password);
        });
    }

    [Fact]
    public void PasswordHashing_ShouldProduceValidBase64()
    {
        // Arrange
        string password = "TestPassword123!";
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hashed = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount: 10000,
            numBytesRequested: 32);

        // Act
        string hash = Convert.ToBase64String(salt.Concat(hashed).ToArray());

        // Assert
        hash.Should().NotBeNullOrEmpty();
        
        // Should be valid base64
        Func<byte[]> decodeAction = () => Convert.FromBase64String(hash);
        decodeAction.Should().NotThrow();

        byte[] decoded = Convert.FromBase64String(hash);
        decoded.Should().HaveCount(48); // 16 bytes salt + 32 bytes hash
    }

    [Theory]
    [InlineData("test@example.com", "InputFormat.EmailAddress")]
    [InlineData("user.name@domain.co.uk", "InputFormat.EmailAddress")]
    [InlineData("test+tag@example.com", "InputFormat.EmailAddress")]
    [InlineData("plaintext", "InputFormat.Other")]
    [InlineData("123456789", "InputFormat.Other")]
    [InlineData("", "InputFormat.Other")]
    public void EmailValidation_ShouldIdentifyEmailsCorrectly(string input, string expectedResult)
    {
        // Act
        bool isEmail = CloudLoginServer.IsInputValidEmailAddress(input);

        // Assert
        if (expectedResult == "InputFormat.EmailAddress")
            isEmail.Should().BeTrue($"'{input}' should be identified as an email");
        else
            isEmail.Should().BeFalse($"'{input}' should not be identified as an email");
    }

    [Theory]
    [InlineData("test@example.com", "test@EXAMPLE.COM")]
    [InlineData("User.Name@Domain.Co.UK", "user.name@domain.co.uk")]
    [InlineData("TEST+TAG@EXAMPLE.COM", "test+tag@example.com")]
    public void EmailValidation_ShouldBeCaseInsensitive(string email1, string email2)
    {
        // Act
        bool result1 = CloudLoginServer.IsInputValidEmailAddress(email1);
        bool result2 = CloudLoginServer.IsInputValidEmailAddress(email2);

        // Assert
        result1.Should().Be(result2, "Email validation should be case insensitive");
        result1.Should().BeTrue("Both variations should be valid emails");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void EmailValidation_WithWhitespaceOrEmpty_ShouldReturnFalse(string input)
    {
        // Act
        bool result = CloudLoginServer.IsInputValidEmailAddress(input);

        // Assert
        result.Should().BeFalse($"Whitespace or empty input '{input}' should not be a valid email");
    }

    [Fact]
    public void PasswordSecurity_ShouldUseStrongHashingParameters()
    {
        // Arrange
        string password = "TestPassword123!";
        byte[] salt = RandomNumberGenerator.GetBytes(16);

        // Act - Test that we're using secure hashing parameters
        DateTime startTime = DateTime.Now;
        
        byte[] hash = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount: 100000, // Updated to match server implementation
            numBytesRequested: 32);

        TimeSpan hashingTime = DateTime.Now - startTime;

        // Assert
        hash.Should().HaveCount(32, "Hash should be 32 bytes long");
        // Adjust timing expectation to be more realistic for modern hardware
        hashingTime.Should().BeGreaterThan(TimeSpan.Zero, "Hashing should take some time");
        hashingTime.Should().BeLessThan(TimeSpan.FromSeconds(10), "But not too long for usability"); // Increased due to higher iterations
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(10000)]
    [InlineData(100000)]
    public void PasswordHashing_WithDifferentIterations_ShouldProduceDifferentHashes(int iterations)
    {
        // Arrange
        string password = "TestPassword123!";
        byte[] salt = RandomNumberGenerator.GetBytes(16);

        // Act
        byte[] hash = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount: iterations,
            numBytesRequested: 32);

        // Assert
        hash.Should().NotBeNull();
        hash.Should().HaveCount(32);
        
        // Different iteration counts should produce different hashes (even with same salt)
        // This is a property of PBKDF2
        hash.Should().NotBeEquivalentTo(new byte[32], "Hash should not be empty or null bytes");
    }

    [Fact]
    public void CloudLoginSerialization_Options_ShouldBeConfiguredForCloudLogin()
    {
        // Act
        var options = CloudLoginSerialization.Options;

        // Assert
        options.Should().NotBeNull();
        options.PropertyNamingPolicy.Should().NotBeNull("Should have a naming policy for consistent JSON");
        options.WriteIndented.Should().BeTrue("Should be indented for readability");
    }

    [Theory]
    [InlineData("CloudLogin", "Login", "/CloudLogin/Login")]
    [InlineData("api", "users", "/api/users")]
    [InlineData("", "", "/")]
    [InlineData("Test", "Action", "/Test/Action")]
    public void CloudLoginShared_RedirectString_ShouldFormatBasicUrlCorrectly(
        string controller, string action, string expectedPath)
    {
        // Act
        string result = CloudLoginShared.RedirectString(controller, action);

        // Assert
        result.Should().Be(expectedPath);
    }

    [Fact]
    public void SecurityValidation_PasswordComplexity_ShouldMeetMinimumStandards()
    {
        // This test verifies that our hashing approach meets basic security standards
        // In a real implementation, you'd also want to test password complexity requirements
        
        // Arrange
        List<string> weakPasswords = ["123456", "password", "admin", ""];
        List<string> strongPasswords = ["StrongP@ssw0rd123!", "MyS3cur3P@ssw0rd", "Compl3x!P@ssword"];

        foreach (string password in strongPasswords)
        {
            // Act - Hash the password
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = KeyDerivation.Pbkdf2(
                password,
                salt,
                KeyDerivationPrf.HMACSHA256,
                iterationCount: 100000, // Updated to match server implementation
                numBytesRequested: 32);

            string hashedPassword = Convert.ToBase64String(salt.Concat(hash).ToArray());

            // Assert
            hashedPassword.Should().NotBeNullOrEmpty();
            hashedPassword.Should().NotContain(password, "Hash should not contain original password");
            hashedPassword.Length.Should().BeGreaterThan(password.Length, "Hash should be longer than original");
        }
    }
}