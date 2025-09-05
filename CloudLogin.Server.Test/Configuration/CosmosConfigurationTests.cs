using System.Text.Json;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Serialization;
using Xunit;

namespace CloudLogin.Server.Test.Configuration;

public class CosmosConfigurationTests
{
    [Fact]
    public void BaseRecord_Should_Use_Default_Property_Names_When_No_Configuration()
    {
        // Arrange
        BaseRecord.CosmosConfiguration = null;
        
        // Act
        var typePropertyName = BaseRecord.GetTypePropertyName();
        var partitionKeyPropertyName = BaseRecord.GetPartitionKeyPropertyName();
        var partitionKeyPath = BaseRecord.GetPartitionKeyPath();
        
        // Assert
        Assert.Equal("$type", typePropertyName);
        Assert.Equal("/pk", partitionKeyPropertyName);
        Assert.Equal("/pk", partitionKeyPath);
    }
    
    [Fact]
    public void BaseRecord_Should_Use_Custom_Property_Names_When_Configured()
    {
        // Arrange
        BaseRecord.CosmosConfiguration = new CosmosConfiguration
        {
            TypeName = "documentType",
            PartitionKeyName = "/customPartition"
        };
        
        // Act
        var typePropertyName = BaseRecord.GetTypePropertyName();
        var partitionKeyPropertyName = BaseRecord.GetPartitionKeyPropertyName();
        var partitionKeyPath = BaseRecord.GetPartitionKeyPath();
        var jsonPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName();
        
        // Assert
        Assert.Equal("documentType", typePropertyName);
        Assert.Equal("/customPartition", partitionKeyPropertyName);
        Assert.Equal("/customPartition", partitionKeyPath);
        Assert.Equal("customPartition", jsonPropertyName);
    }
    
    [Fact]
    public void UserInfo_Should_Serialize_With_Default_Property_Names_And_Preserve_Casing()
    {
        // Arrange
        BaseRecord.CosmosConfiguration = null; // Use defaults
        
        var userInfo = new UserInfo
        {
            ID = Guid.NewGuid(),
            FirstName = "John",
            LastName = "Doe",
            DisplayName = "John Doe"
        };
        
        var serializer = new ConfigurableCosmosSerializer();
        
        // Act
        using var stream = serializer.ToStream(userInfo);
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        
        var document = JsonDocument.Parse(json);
        
        // Assert - Check that it uses $type as default and preserves PascalCase property names
        Assert.True(document.RootElement.TryGetProperty("$type", out var typeProperty), "Should have $type property");
        Assert.Equal("UserInfo", typeProperty.GetString());
        
        Assert.True(document.RootElement.TryGetProperty("pk", out var pkProperty), "Should have pk property");
        Assert.Equal("UserInfo", pkProperty.GetString());
        
        Assert.True(document.RootElement.TryGetProperty("id", out var idProperty), "Should have id property");
        Assert.Equal(userInfo.ID.ToString(), idProperty.GetString());
        
        // Check that property names are preserved in original casing (PascalCase)
        Assert.True(document.RootElement.TryGetProperty("FirstName", out var firstNameProperty), "Should have FirstName property");
        Assert.Equal("John", firstNameProperty.GetString());
        
        Assert.True(document.RootElement.TryGetProperty("LastName", out var lastNameProperty), "Should have LastName property");
        Assert.Equal("Doe", lastNameProperty.GetString());
        
        Assert.True(document.RootElement.TryGetProperty("DisplayName", out var displayNameProperty), "Should have DisplayName property");
        Assert.Equal("John Doe", displayNameProperty.GetString());
    }
    
    [Fact]
    public void UserInfo_Should_Serialize_With_Custom_Property_Names()
    {
        // Arrange
        BaseRecord.CosmosConfiguration = new CosmosConfiguration
        {
            TypeName = "docType",
            PartitionKeyName = "/customPk"
        };
        
        var userInfo = new UserInfo
        {
            ID = Guid.NewGuid(),
            FirstName = "John",
            LastName = "Doe",
            DisplayName = "John Doe"
        };
        
        var serializer = new ConfigurableCosmosSerializer();
        
        // Act
        using var stream = serializer.ToStream(userInfo);
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        
        var document = JsonDocument.Parse(json);
        
        // Assert
        Assert.True(document.RootElement.TryGetProperty("docType", out var typeProperty));
        Assert.Equal("UserInfo", typeProperty.GetString());
        
        Assert.True(document.RootElement.TryGetProperty("customPk", out var pkProperty));
        Assert.Equal("UserInfo", pkProperty.GetString());
        
        Assert.True(document.RootElement.TryGetProperty("id", out var idProperty));
        Assert.Equal(userInfo.ID.ToString(), idProperty.GetString());
        
        Assert.True(document.RootElement.TryGetProperty("FirstName", out var firstNameProperty));
        Assert.Equal("John", firstNameProperty.GetString());
    }
    
    [Fact]
    public void UserInfo_Should_Deserialize_With_Custom_Property_Names()
    {
        // Arrange
        BaseRecord.CosmosConfiguration = new CosmosConfiguration
        {
            TypeName = "docType",
            PartitionKeyName = "/customPk"
        };
        
        var testId = Guid.NewGuid();
        var json = $$"""
        {
            "id": "{{testId}}",
            "docType": "UserInfo",
            "customPk": "UserInfo",
            "FirstName": "Jane",
            "LastName": "Smith",
            "DisplayName": "Jane Smith",
            "IsLocked": false,
            "CreatedOn": "2024-01-01T00:00:00Z",
            "LastSignedIn": "2024-01-01T00:00:00Z",
            "Inputs": []
        }
        """;
        
        var serializer = new ConfigurableCosmosSerializer();
        
        // Act
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var userInfo = serializer.FromStream<UserInfo>(stream);
        
        // Assert
        Assert.NotNull(userInfo);
        Assert.Equal(testId, userInfo.ID);
        Assert.Equal("UserInfo", userInfo.Type);
        Assert.Equal("UserInfo", userInfo.PartitionKey);
        Assert.Equal("Jane", userInfo.FirstName);
        Assert.Equal("Smith", userInfo.LastName);
        Assert.Equal("Jane Smith", userInfo.DisplayName);
    }
}