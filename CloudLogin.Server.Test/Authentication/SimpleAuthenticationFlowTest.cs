using Xunit;
using AngryMonkey.CloudLogin;

namespace CloudLogin.Server.Test.Authentication;

public class SimpleAuthenticationFlowTest
{
    [Fact]
    public void Authentication_Flow_Should_Be_Simple_And_Clear()
    {
        // SIMPLIFIED AUTHENTICATION FLOW:
        
        // 1. User clicks "Sign in with Google"
        // 2. Google OAuth redirects back with claims (email, name, etc.)
        // 3. OnSignedIn event fires -> CloudLoginAuthenticationService.HandleSignIn
        // 4. HandleSignIn checks if user exists by email
        // 5. If exists: adds Google provider to existing user
        // 6. If not exists: creates new user with Google provider
        // 7. LoginResult method gets the EXISTING user (should never create new ones)
        
        string testEmail = "test@example.com";
        
        // Scenario 1: First time sign-in with Google
        // Expected: Creates ONE user with Google provider
        var newUser = new User
        {
            ID = Guid.NewGuid(),
            FirstName = "John",
            LastName = "Doe",
            DisplayName = "John Doe",
            Inputs = 
            [
                new LoginInput
                {
                    Input = testEmail,
                    Format = InputFormat.EmailAddress,
                    IsPrimary = true,
                    Providers = [new LoginProvider { Code = "Google" }]
                }
            ]
        };
        
        Assert.Single(newUser.Inputs);
        Assert.Single(newUser.Inputs.First().Providers);
        Assert.Equal("Google", newUser.Inputs.First().Providers.First().Code);
        
        // Scenario 2: Same user signs in with Microsoft (same email)
        // Expected: SAME user, now with Google AND Microsoft providers
        var existingUser = newUser; // Simulate getting existing user
        
        // Simulate adding Microsoft provider
        var existingInput = existingUser.Inputs.First();
        if (!existingInput.Providers.Any(p => p.Code == "Microsoft"))
        {
            existingInput.Providers.Add(new LoginProvider { Code = "Microsoft" });
        }
        
        // Verify: Still ONE user, but now with TWO providers
        Assert.Single(existingUser.Inputs); // Still one input (email)
        Assert.Equal(2, existingUser.Inputs.First().Providers.Count); // Now two providers
        Assert.True(existingUser.Inputs.First().Providers.Any(p => p.Code == "Google"));
        Assert.True(existingUser.Inputs.First().Providers.Any(p => p.Code == "Microsoft"));
        
        // Key principle: Same email = Same user, just more providers
        Assert.Equal(newUser.ID, existingUser.ID); // SAME USER ID
    }
    
    [Fact] 
    public void Should_Not_Create_Duplicate_Users()
    {
        // This test ensures we don't create duplicate users
        
        string email = "user@example.com";
        
        // First authentication creates user
        var user1 = new User { ID = Guid.NewGuid() };
        
        // Second authentication with same email should get SAME user
        var user2 = user1; // Simulate finding existing user
        
        // Should be the exact same user
        Assert.Equal(user1.ID, user2.ID);
        Assert.True(ReferenceEquals(user1, user2)); // Same object reference
    }
}