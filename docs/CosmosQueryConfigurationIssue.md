# Cosmos Configuration Query Issue

## The Problem You Identified

You're absolutely correct! The issue is that while we've configured the JSON serialization to use custom property names like:

```json
{
  "id": "guid",
  "customType": "UserInfo",    // Instead of "$type"
  "customPk": "UserInfo"       // Instead of "pk"
}
```

The **query logic in CosmosMethods is still using hardcoded values**:

```csharp
// PROBLEM: These are hardcoded
IQueryable<UserInfo> usersQueryable = Queryable<UserInfo>("UserInfo", user => ...);
// Uses hardcoded "UserInfo" as partition key value

.Where(item => item.Type == typeof(T).Name);
// Uses hardcoded "UserInfo" as type value
```

## What Should Happen

When custom configuration is provided:

```csharp
CosmosConfiguration = new CosmosConfiguration
{
    TypeName = "documentType",           // Custom type property name
    PartitionKeyName = "/customPartition" // Custom partition key name
};
```

The queries should:

1. **Use configured type values**: Instead of hardcoded `"UserInfo"`
2. **Use configured partition key values**: Instead of hardcoded `"UserInfo"`
3. **Query with correct property names**: Use `documentType` instead of `$type`

## Current State vs Expected State

### Current (Wrong)
```csharp
// Always queries with hardcoded values
Queryable<UserInfo>("UserInfo", ...)  // Hardcoded partition key
.Where(item => item.Type == "UserInfo")  // Hardcoded type
```

### Expected (Correct)
```csharp
// Should query with configured values
Queryable<UserInfo>(GetConfiguredPartitionKey(), ...)
.Where(item => item.Type == GetConfiguredTypeName())
```

## Why This Matters

1. **Configuration Ignored**: Custom configurations are ignored during queries
2. **Query Failures**: Queries fail when using custom property names
3. **Data Inconsistency**: Can't find records stored with custom schemas

## Solution Needed

The CosmosMethods class needs to:
1. Respect the configured type and partition key values
2. Use the configuration from BaseRecord.CosmosConfiguration
3. Query using the actual values stored in the database (not hardcoded ones)

This is exactly what you've identified - the root cause of the authentication issues!