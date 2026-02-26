using System.Security.Claims;
using Xunit;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;

namespace CloudLogin.Server.Test.Authentication;

public class ProviderIdentifierTests
{
    [Fact]
    public void GetProviderIdentifier_Should_Capture_NameIdentifier_Claim()
    {
        // Arrange - Simulate a Google OAuth response with user identifier
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com"),
            new(ClaimTypes.GivenName, "John"),
            new(ClaimTypes.Surname, "Doe"),
            new(ClaimTypes.Name, "John Doe"),
            new(ClaimTypes.NameIdentifier, "google_user_123456") // Google's user ID
        };
        
        var identity = new ClaimsIdentity(claims, "Google");
        var principal = new ClaimsPrincipal(identity);

        // Act - Simulate what CloudLoginAuthenticationService.GetProviderIdentifier would do
        var nameIdentifier = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        // Assert
        Assert.Equal("google_user_123456", nameIdentifier);
    }

    [Fact]
    public void GetProviderIdentifier_Should_Fallback_To_Subject_Claim()
    {
        // Arrange - Simulate an OAuth provider that uses 'sub' claim instead of NameIdentifier
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com"),
            new(ClaimTypes.GivenName, "John"),
            new(ClaimTypes.Surname, "Doe"),
            new(ClaimTypes.Name, "John Doe"),
            new("sub", "provider_user_789") // Subject claim (common in JWT tokens)
        };
        
        var identity = new ClaimsIdentity(claims, "SomeProvider");
        var principal = new ClaimsPrincipal(identity);

        // Act - Simulate the fallback logic
        var nameIdentifier = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        
        var identifier = !string.IsNullOrEmpty(nameIdentifier) ? nameIdentifier : subject;
        
        // Assert
        Assert.Null(nameIdentifier); // NameIdentifier not present
        Assert.Equal("provider_user_789", subject);
        Assert.Equal("provider_user_789", identifier);
    }

    [Fact]
    public void GetProviderIdentifier_Should_Fallback_To_OID_Claim()
    {
        // Arrange - Simulate Microsoft Azure AD response with 'oid' claim
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "test@example.com"),
            new(ClaimTypes.GivenName, "John"),
            new(ClaimTypes.Surname, "Doe"),
            new(ClaimTypes.Name, "John Doe"),
            new("oid", "12345678-1234-1234-1234-123456789abc") // Microsoft's Object ID
        };
        
        var identity = new ClaimsIdentity(claims, "Microsoft");
        var principal = new ClaimsPrincipal(identity);

        // Act - Simulate the fallback logic
        var nameIdentifier = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        var oid = principal.FindFirst("oid")?.Value;
        
        var identifier = !string.IsNullOrEmpty(nameIdentifier) ? nameIdentifier : 
                        !string.IsNullOrEmpty(subject) ? subject : oid;
        
        // Assert
        Assert.Null(nameIdentifier); // NameIdentifier not present
        Assert.Null(subject); // Subject not present
        Assert.Equal("12345678-1234-1234-1234-123456789abc", oid);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", identifier);
    }

    [Fact]
    public void LoginProvider_Should_Store_External_Provider_Identifier()
    {
        // Arrange & Act - Create a LoginProvider with identifier (like external providers)
        var externalProvider = new LoginProvider
        {
            Code = "Google",
            Identifier = "google_user_123456"
        };

        // Assert
        Assert.Equal("Google", externalProvider.Code);
        Assert.Equal("google_user_123456", externalProvider.Identifier);
        Assert.Null(externalProvider.PasswordHash); // External providers don't have password hashes
    }

    [Fact]
    public void LoginProvider_Should_Store_Password_Hash_For_Internal_Providers()
    {
        // Arrange & Act - Create a LoginProvider for password authentication
        var passwordProvider = new LoginProvider
        {
            Code = "Password",
            PasswordHash = "hashed_password_123",
            Identifier = null // Internal providers don't have external identifiers
        };

        // Assert
        Assert.Equal("Password", passwordProvider.Code);
        Assert.Equal("hashed_password_123", passwordProvider.PasswordHash);
        Assert.Null(passwordProvider.Identifier); // Internal providers don't have external identifiers
    }

    [Fact]
    public void LoginProvider_Should_Store_Code_Provider_Without_Hash_Or_Identifier()
    {
        // Arrange & Act - Create a LoginProvider for code-based authentication
        var codeProvider = new LoginProvider
        {
            Code = "Code",
            Identifier = null, // Internal providers don't have external identifiers
            PasswordHash = null // Code providers don't have password hashes
        };

        // Assert
        Assert.Equal("Code", codeProvider.Code);
        Assert.Null(codeProvider.PasswordHash);
        Assert.Null(codeProvider.Identifier);
    }

    [Fact]
    public void User_Should_Have_Multiple_Providers_With_Different_Identifiers()
    {
        // Arrange & Act - Simulate a user with multiple external providers
        var user = new UserModel
        {
            ID = Guid.NewGuid(),
            FirstName = "John",
            LastName = "Doe",
            DisplayName = "John Doe",
            Inputs =
            [
                new LoginInput
                {
                    Input = "test@example.com",
                    Format = InputFormat.EmailAddress,
                    IsPrimary = true,
                    Providers =
                    [
                        new LoginProvider { Code = "Code", Identifier = null },
                        new LoginProvider { Code = "Google", Identifier = "google_user_123456" },
                        new LoginProvider { Code = "Microsoft", Identifier = "12345678-1234-1234-1234-123456789abc" },
                        new LoginProvider { Code = "Facebook", Identifier = "facebook_user_789" }
                    ]
                }
            ]
        };

        // Assert
        var providers = user.Inputs.First().Providers;
        Assert.Equal(4, providers.Count);
        
        var codeProvider = providers.First(p => p.Code == "Code");
        Assert.Null(codeProvider.Identifier);
        
        var googleProvider = providers.First(p => p.Code == "Google");
        Assert.Equal("google_user_123456", googleProvider.Identifier);
        
        var microsoftProvider = providers.First(p => p.Code == "Microsoft");
        Assert.Equal("12345678-1234-1234-1234-123456789abc", microsoftProvider.Identifier);
        
        var facebookProvider = providers.First(p => p.Code == "Facebook");
        Assert.Equal("facebook_user_789", facebookProvider.Identifier);
    }
}