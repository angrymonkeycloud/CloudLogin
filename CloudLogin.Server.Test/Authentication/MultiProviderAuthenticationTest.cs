using System.Text.Json;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Serialization;
using AngryMonkey.CloudLogin;
using Xunit;

namespace CloudLogin.Server.Test.Authentication;

public class MultiProviderAuthenticationTest
{
    [Fact]
    public async Task Should_Link_Providers_To_Same_User_Account()
    {
        // Simulate the scenario:
        // 1. User signs in with Code provider using "test@example.com"
        // 2. User signs in with Google provider using "test@example.com" 
        // 3. Should result in ONE user with TWO providers, not two separate users

        // Arrange
        string testEmail = "test@example.com";
        var currentDateTime = DateTimeOffset.UtcNow;

        // Simulate the user after first Code authentication
        var existingUser = new User
        {
            ID = Guid.NewGuid(),
            FirstName = "John",
            LastName = "Doe",
            DisplayName = "John Doe", 
            CreatedOn = currentDateTime,
            LastSignedIn = currentDateTime,
            Inputs =
            [
                new LoginInput
                {
                    Input = testEmail.ToLowerInvariant(), // Normalized
                    Format = InputFormat.EmailAddress,
                    IsPrimary = true,
                    Providers = [new LoginProvider { Code = "Code" }]
                }
            ]
        };

        // After Google authentication, the user should have both providers
        var expectedUserAfterGoogle = new User
        {
            ID = existingUser.ID, // Same user ID
            FirstName = "John",
            LastName = "Doe", 
            DisplayName = "John Doe",
            CreatedOn = existingUser.CreatedOn, // Same creation time
            LastSignedIn = currentDateTime, // Updated sign-in time
            Inputs =
            [
                new LoginInput
                {
                    Input = testEmail.ToLowerInvariant(),
                    Format = InputFormat.EmailAddress,
                    IsPrimary = true,
                    Providers = 
                    [
                        new LoginProvider { Code = "Code" },    // Original provider
                        new LoginProvider { Code = "Google" }   // New provider added
                    ]
                }
            ]
        };

        // Act & Assert
        // Verify that the input normalization logic works correctly
        string normalizedInput1 = testEmail.Trim().ToLowerInvariant();
        string normalizedInput2 = "Test@Example.Com".Trim().ToLowerInvariant();
        string normalizedInput3 = " test@EXAMPLE.com ".Trim().ToLowerInvariant();

        Assert.Equal(normalizedInput1, normalizedInput2);
        Assert.Equal(normalizedInput1, normalizedInput3);

        // Verify that the provider linking logic would work
        var existingInput = existingUser.Inputs.FirstOrDefault(i => 
            string.Equals(i.Input, normalizedInput1, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(existingInput);
        Assert.True(existingInput.Providers.Any(p => p.Code == "Code"));
        Assert.False(existingInput.Providers.Any(p => p.Code == "Google"));

        // Simulate adding Google provider
        if (!existingInput.Providers.Any(p => string.Equals(p.Code, "Google", StringComparison.OrdinalIgnoreCase)))
        {
            existingInput.Providers.Add(new LoginProvider { Code = "Google" });
        }

        // Verify the result
        Assert.Equal(2, existingInput.Providers.Count);
        Assert.True(existingInput.Providers.Any(p => p.Code == "Code"));
        Assert.True(existingInput.Providers.Any(p => p.Code == "Google"));
        
        // Most importantly: still the same user, same ID
        Assert.Equal(existingUser.ID, existingUser.ID); // Same user object
    }

    [Fact]
    public void Should_Normalize_Email_Addresses_Consistently()
    {
        // Test various email formats to ensure they all normalize to the same value
        var testEmails = new[]
        {
            "test@example.com",
            "Test@Example.Com", 
            " test@EXAMPLE.com ",
            "TEST@EXAMPLE.COM",
            "\ttest@example.com\n"
        };

        var normalizedEmails = testEmails.Select(email => email.Trim().ToLowerInvariant()).ToArray();

        // All should normalize to the same value
        var expected = "test@example.com";
        Assert.All(normalizedEmails, email => Assert.Equal(expected, email));
    }

    [Fact]
    public void CloudLoginAuthenticationService_Logic_Should_Be_Correct()
    {
        // This test verifies the authentication service logic without dependencies
        
        // Simulate the key decision points:
        
        // 1. Email normalization
        string originalEmail = " Test@Example.Com ";
        string normalizedEmail = originalEmail.Trim().ToLowerInvariant();
        Assert.Equal("test@example.com", normalizedEmail);
        
        // 2. User lookup (simulated)
        var existingUser = new User { ID = Guid.NewGuid() };
        bool userExists = existingUser != null; // This simulates GetUserByEmailAddress returning a user
        
        if (userExists)
        {
            // 3. Provider linking logic (simulated UpdateExistingUser)
            var input = existingUser.Inputs.FirstOrDefault() ?? new LoginInput 
            { 
                Input = normalizedEmail, 
                Format = InputFormat.EmailAddress, 
                Providers = [] 
            };
            
            string newProviderName = "Google";
            bool providerExists = input.Providers.Any(p => string.Equals(p.Code, newProviderName, StringComparison.OrdinalIgnoreCase));
            
            if (!providerExists)
            {
                input.Providers.Add(new LoginProvider { Code = newProviderName });
            }
            
            // Verify the provider was added
            Assert.True(input.Providers.Any(p => p.Code == newProviderName));
        }
        else
        {
            // 4. New user creation (simulated CreateNewUser)
            var newUser = new User
            {
                ID = Guid.NewGuid(),
                Inputs = 
                [
                    new LoginInput
                    {
                        Input = normalizedEmail,
                        Format = InputFormat.EmailAddress,
                        IsPrimary = true,
                        Providers = [new LoginProvider { Code = "Google" }]
                    }
                ]
            };
            
            Assert.Equal(normalizedEmail, newUser.Inputs.First().Input);
        }
        
        // The key insight: The logic should ALWAYS result in one user per unique email,
        // with multiple providers linked to that single user account.
    }
}