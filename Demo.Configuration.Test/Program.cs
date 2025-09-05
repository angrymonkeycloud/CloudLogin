using System.Text.Json;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Serialization;

// Demonstrate default configuration
Console.WriteLine("=== Default Configuration ===");
BaseRecord.CosmosConfiguration = null;

Console.WriteLine($"Type Property Name: {BaseRecord.GetTypePropertyName()}");
Console.WriteLine($"Partition Key Name: {BaseRecord.GetPartitionKeyPropertyName()}");
Console.WriteLine($"Partition Key Path: {BaseRecord.GetPartitionKeyPath()}");
Console.WriteLine($"JSON Property Name: {BaseRecord.GetPartitionKeyJsonPropertyName()}");

// Create and serialize a UserInfo with default settings
var user1 = new UserInfo
{
    ID = Guid.NewGuid(),
    FirstName = "John",
    LastName = "Doe",
    DisplayName = "John Doe"
};

var serializer = new ConfigurableCosmosSerializer();
using var stream1 = serializer.ToStream(user1);
using var reader1 = new StreamReader(stream1);
var json1 = reader1.ReadToEnd();

Console.WriteLine($"Serialized JSON (default): {json1}");
Console.WriteLine();

// Demonstrate custom configuration
Console.WriteLine("=== Custom Configuration ===");
BaseRecord.CosmosConfiguration = new CosmosConfiguration
{
    TypeName = "documentType",
    PartitionKeyName = "/customPartition"
};

Console.WriteLine($"Type Property Name: {BaseRecord.GetTypePropertyName()}");
Console.WriteLine($"Partition Key Name: {BaseRecord.GetPartitionKeyPropertyName()}");
Console.WriteLine($"Partition Key Path: {BaseRecord.GetPartitionKeyPath()}");
Console.WriteLine($"JSON Property Name: {BaseRecord.GetPartitionKeyJsonPropertyName()}");

// Create and serialize a UserInfo with custom settings
var user2 = new UserInfo
{
    ID = Guid.NewGuid(),
    FirstName = "Jane",
    LastName = "Smith",
    DisplayName = "Jane Smith"
};

using var stream2 = serializer.ToStream(user2);
using var reader2 = new StreamReader(stream2);
var json2 = reader2.ReadToEnd();

Console.WriteLine($"Serialized JSON (custom): {json2}");
Console.WriteLine();

// Demonstrate round-trip serialization
Console.WriteLine("=== Round-trip Serialization Test ===");
using var streamBack = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json2));
var userBack = serializer.FromStream<UserInfo>(streamBack);

Console.WriteLine($"Deserialized User:");
Console.WriteLine($"  ID: {userBack.ID}");
Console.WriteLine($"  Type: {userBack.Type}");
Console.WriteLine($"  PartitionKey: {userBack.PartitionKey}");
Console.WriteLine($"  Name: {userBack.FirstName} {userBack.LastName}");
Console.WriteLine($"  Display Name: {userBack.DisplayName}");

Console.WriteLine("\nConfiguration test completed successfully!");