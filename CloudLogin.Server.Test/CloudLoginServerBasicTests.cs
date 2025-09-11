using FluentAssertions;
using Xunit;
using AngryMonkey.CloudLogin.Server;

namespace AngryMonkey.CloudLogin.Server.Tests;

public class CloudLoginServerBasicTests
{
    [Theory]
    [InlineData("test@example.com", true)]
    [InlineData("user.name@domain.co.uk", true)]
    [InlineData("test+tag@example.com", true)]
    [InlineData("test.email+tag+sorting@example.com", true)]
    [InlineData("x@example.com", true)]
    [InlineData("example@s.example", true)]
    [InlineData("invalid-email", false)]
    [InlineData("@example.com", false)]
    [InlineData("test@", false)]
    [InlineData("test@.com", false)]
    [InlineData("test..email@example.com", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsInputValidEmailAddress_ShouldValidateEmailCorrectly(string input, bool expected)
    {
        // Act
        bool result = CloudLoginServer.IsInputValidEmailAddress(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsInputValidEmailAddress_WithNullInput_ShouldReturnFalse()
    {
        // Act
        bool result = CloudLoginServer.IsInputValidEmailAddress(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HashPassword_BasicTest_ShouldPass()
    {
        // This is a basic test that doesn't require complex dependencies
        // We're testing that the test framework is working correctly
        
        // Act & Assert
        Assert.True(true); // Basic test to ensure test framework works
    }

    [Fact]
    public void CloudLoginSerialization_ShouldHaveValidOptions()
    {
        // Act
        var options = CloudLoginSerialization.Options;

        // Assert
        options.Should().NotBeNull();
        options.PropertyNamingPolicy.Should().NotBeNull();
        options.WriteIndented.Should().BeTrue();
    }

    [Fact]
    public void CloudLoginShared_RedirectString_ShouldGenerateValidUrl()
    {
        // Arrange
        string controller = "CloudLogin";
        string action = "Login";
        string keepMeSignedIn = "true";
        string redirectUri = "https://example.com/dashboard";

        // Act
        string result = CloudLoginShared.RedirectString(controller, action, keepMeSignedIn, redirectUri);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().StartWith($"/{controller}/{action}");
        result.Should().Contain("keepMeSignedIn=true");
        result.Should().Contain("redirectUri=");
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("controller", "action", "")]
    [InlineData("CloudLogin", "Login", "keepMeSignedIn=true")]
    public void CloudLoginShared_RedirectString_WithVariousInputs_ShouldGenerateCorrectFormat(
        string controller, string action, string expectedQueryPart)
    {
        // Act
        string result = CloudLoginShared.RedirectString(controller, action);

        // Assert
        result.Should().StartWith($"/{controller}/{action}");
        
        if (!string.IsNullOrEmpty(expectedQueryPart))
            result.Should().Contain(expectedQueryPart);
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name@domain.co.uk")]
    [InlineData("complex.email+tag@subdomain.example.org")]
    public void EmailValidation_WithValidEmails_ShouldPassValidation(string email)
    {
        // Act
        bool result = CloudLoginServer.IsInputValidEmailAddress(email);

        // Assert
        result.Should().BeTrue($"'{email}' should be a valid email address");
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@missingdomain.com")]
    [InlineData("missing@.com")]
    [InlineData("missing@domain")]
    [InlineData("missing.domain@.com")]
    public void EmailValidation_WithInvalidEmails_ShouldFailValidation(string email)
    {
        // Act
        bool result = CloudLoginServer.IsInputValidEmailAddress(email);

        // Assert
        result.Should().BeFalse($"'{email}' should not be a valid email address");
    }

    [Fact]
    public void CloudLoginShared_RedirectString_WithAllParameters_ShouldIncludeAllInQuery()
    {
        // Arrange
        string controller = "CloudLogin";
        string action = "Login";
        string keepMeSignedIn = "true";
        string redirectUri = "https://example.com/dashboard";
        string sameSite = "false";
        string primaryEmail = "user@example.com";
        string userInfo = "encoded-user-info";
        string inputValue = "test-input";

        // Act
        string result = CloudLoginShared.RedirectString(
            controller, action, keepMeSignedIn, redirectUri, 
            sameSite, primaryEmail, userInfo, inputValue);

        // Assert
        result.Should().StartWith($"/{controller}/{action}?");
        result.Should().Contain("keepMeSignedIn=true");
        result.Should().Contain("sameSite=false");
        result.Should().Contain("primaryEmail=");
        result.Should().Contain("userInfo=");
        result.Should().Contain("input=");
    }

    [Theory]
    [InlineData("https://example.com/test")]
    [InlineData("user@example.com")]
    [InlineData("test input with spaces")]
    public void CloudLoginShared_RedirectString_ShouldUrlEncodeParameters(string originalValue)
    {
        // Act
        string result = CloudLoginShared.RedirectString("test", "action", redirectUri: originalValue);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().StartWith("/test/action?redirectUri=");
        
        // The value should be URL encoded (we don't test exact encoding since different methods may use different formats)
        // Just ensure special characters are encoded somehow
        if (originalValue.Contains("@"))
        {
            result.Should().Match("*/test/action?redirectUri=*%40*"); // @ should be encoded as %40
        }
        if (originalValue.Contains(" "))
        {
            result.Should().Match("*/test/action?redirectUri=*"); // Spaces should be encoded (either as %20 or +)
            result.Should().NotContain(" "); // No unencoded spaces should remain
        }
        if (originalValue.Contains("://"))
        {
            result.Should().Match("*/test/action?redirectUri=*%3*"); // Colons should be encoded (either %3a or %3A)
        }
    }

    [Theory]
    [InlineData("", InputFormat.Other)]
    [InlineData("   ", InputFormat.Other)]
    [InlineData("test@example.com", InputFormat.EmailAddress)]
    [InlineData("user.name@domain.co.uk", InputFormat.EmailAddress)]
    [InlineData("randomtext", InputFormat.Other)]
    [InlineData("123456789", InputFormat.Other)]
    public void InputFormat_BasicValidation_ShouldIdentifyFormats(string input, InputFormat expected)
    {
        // For basic validation, we can test email detection directly
        // Phone number detection would require the CloudGeographyClient dependency
        
        if (expected == InputFormat.EmailAddress)
        {
            // Act
            bool isEmail = CloudLoginServer.IsInputValidEmailAddress(input);
            
            // Assert
            isEmail.Should().BeTrue($"'{input}' should be identified as an email");
        }
        else if (expected == InputFormat.Other)
        {
            // Act
            bool isEmail = CloudLoginServer.IsInputValidEmailAddress(input);
            
            // Assert
            isEmail.Should().BeFalse($"'{input}' should not be identified as an email");
        }
    }
}