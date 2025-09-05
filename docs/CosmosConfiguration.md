# Cosmos DB Configuration Options

CloudLogin now supports configurable partition key and type property names for Cosmos DB integration. This allows you to customize the JSON property names used in your Cosmos DB documents.

## Default Configuration

By default, CloudLogin uses the following property names:
- **Partition Key**: `/pk` (with JSON property name `pk`)
- **Type Property**: `$type`

## Custom Configuration

You can customize these property names in your `Program.cs` when configuring CloudLogin:

### Option 1: Programmatic Configuration

```csharp
builder.AddCloudLoginWeb(new()
{
    WebConfig = config => { config.PageDefaults.SetTitle("My App"); },
    
    Cosmos = new(builder.Configuration.GetSection("Cosmos"))
    {
        PartitionKeyName = "/customPartitionKey",    // Custom partition key path
        TypeName = "documentType"                    // Custom type property name
    },
    
    Providers = [
        // Your providers here
    ]
});
```

### Option 2: Configuration File

Add the custom property names to your `appsettings.json` or configuration source:

```json
{
  "Cosmos": {
    "ConnectionString": "your-connection-string",
    "DatabaseId": "your-database-id", 
    "ContainerId": "your-container-id",
    "PartitionKeyName": "/customPartitionKey",
    "TypeName": "documentType"
  }
}
```

Then in your `Program.cs`:

```csharp
builder.AddCloudLoginWeb(new()
{
    WebConfig = config => { config.PageDefaults.SetTitle("My App"); },
    Cosmos = new(builder.Configuration.GetSection("Cosmos")), // Will use configured values
    Providers = [
        // Your providers here
    ]
});
```

## Cosmos DB Container Setup

When creating your Cosmos DB container, make sure the partition key path matches your configuration:

- If using default: partition key path should be `/pk`
- If using custom: partition key path should match your `PartitionKeyName` value

## Document Structure

With the default configuration, your documents will look like:

```json
{
  "id": "user-guid",
  "$type": "UserInfo",
  "pk": "UserInfo",
  "firstName": "John",
  "lastName": "Doe"
}
```

With custom configuration (`PartitionKeyName = "/customPk"` and `TypeName = "docType"`):

```json
{
  "id": "user-guid", 
  "docType": "UserInfo",
  "customPk": "UserInfo",
  "firstName": "John",
  "lastName": "Doe"
}
```

## Implementation Details

The implementation uses a custom JSON serialization system that:

1. **Dynamic Property Names**: JSON property names are determined at runtime based on configuration
2. **Custom Cosmos Serializer**: `ConfigurableCosmosSerializer` handles the serialization/deserialization
3. **Type-Safe Converter**: `BaseRecordJsonConverter` ensures proper handling of derived types
4. **Backward Compatibility**: Default values maintain existing behavior

### Architecture Components

- `CosmosConfiguration`: Holds the configuration values
- `BaseRecord`: Abstract base class with static configuration holder
- `BaseRecordJsonConverter`: Custom JSON converter for dynamic property names
- `ConfigurableCosmosSerializer`: Cosmos DB serializer that uses the custom converter

## Important Notes

1. The partition key path (e.g., `/pk`, `/customPk`) is used by Cosmos DB for partitioning
2. The type property name is used internally by CloudLogin to identify document types
3. Changes to these configurations should be made before deploying to production
4. All existing documents must match the new property names if you change them
5. The JSON property names are automatically handled through the custom serialization layer
6. The custom serializer ensures consistent serialization/deserialization across all operations

## Migration Considerations

If you need to change these property names on an existing database:

1. **Plan the Migration**: Create a comprehensive migration strategy
2. **Update Configuration**: Change your CloudLogin configuration
3. **Migrate Data**: Update existing documents to use new property names
4. **Deploy Together**: Deploy configuration and data changes simultaneously

### Example Migration Script

```csharp
// Example: Migrate from default ($type, pk) to custom (docType, customPk)
public async Task MigrateDocuments(Container container)
{
    var query = "SELECT * FROM c";
    using var iterator = container.GetItemQueryIterator<dynamic>(query);
    
    while (iterator.HasMoreResults)
    {
        var response = await iterator.ReadNextAsync();
        foreach (var item in response)
        {
            // Create new document with updated property names
            var newItem = new Dictionary<string, object>();
            
            foreach (var property in item)
            {
                var key = property.Key;
                var value = property.Value;
                
                // Map old property names to new ones
                key = key switch
                {
                    "$type" => "docType",
                    "pk" => "customPk", 
                    _ => key
                };
                
                newItem[key] = value;
            }
            
            // Replace the document
            await container.ReplaceItemAsync(newItem, item.id.ToString());
        }
    }
}
```

## Testing

The implementation includes comprehensive tests that verify:

- Default property name behavior
- Custom configuration handling
- Serialization with custom property names
- Deserialization with custom property names
- Type safety and error handling

Run tests with:
```bash
dotnet test --filter "CosmosConfigurationTests"