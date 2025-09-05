# Cosmos DB Configuration Query Fix

## The Problem You Identified

You were absolutely correct! The issue was that Cosmos DB queries were using **hardcoded property names** instead of the **configured property names** from the CosmosConfiguration.

### What Was Happening

When you configured custom property names:
```csharp
CosmosConfiguration = new CosmosConfiguration
{
    TypeName = "documentType",        // Custom type property name
    PartitionKeyName = "/customPk"   // Custom partition key name  
}
```

The database would store JSON like:
```json
{
  "id": "guid",
  "documentType": "UserInfo",    // Custom type property
  "customPk": "UserInfo"         // Custom partition key property
}
```

But the **queries were still looking for the old property names**:
```sql
-- WRONG - Looking for hardcoded property names
WHERE root["Type"] = "UserInfo" AND root["PartitionKey"] = "UserInfo"

-- But the actual JSON has:
WHERE root["documentType"] = "UserInfo" AND root["customPk"] = "UserInfo"
```

This is exactly why users weren't being found - the queries were looking for the wrong field names!

## The Root Cause

### LINQ Translation Issue
The original code used LINQ queries:
```csharp
// BROKEN - LINQ uses C# property names, not JSON property names
.Where(item => item.Type == typeof(T).Name)
.Where(item => item.PartitionKey == partitionKey)
```

When LINQ translated this to SQL, it used the C# property names (`Type`, `PartitionKey`) instead of the actual JSON property names (`documentType`, `customPk`).

### Your Example Query
The query you showed demonstrates this perfectly:
```sql
{
  "query": "SELECT VALUE root FROM root 
   WHERE ((root[\"Type\"] = \"UserInfo\") AND (root[\"PartitionKey\"] = \"UserInfo\"))"
}
```

It's looking for `root["Type"]` and `root["PartitionKey"]`, but if you configured custom names, these fields don't exist!

## The Fix

### Solution: Raw SQL with Configured Property Names

I replaced all LINQ queries with raw SQL queries that use the configured property names:

```csharp
// FIXED - Use configured property names
string typePropertyName = BaseRecord.GetTypePropertyName();        // Gets "documentType" or "$type"
string partitionKeyPropertyName = BaseRecord.GetPartitionKeyJsonPropertyName(); // Gets "customPk" or "pk"

string sql = $"SELECT VALUE root FROM root WHERE root[\"{typePropertyName}\"] = \"UserInfo\" AND root[\"{partitionKeyPropertyName}\"] = \"UserInfo\"";
```

### Before vs After

**Before (Broken)**:
```sql
-- Always looked for hardcoded names
WHERE root["Type"] = "UserInfo" AND root["PartitionKey"] = "UserInfo"
```

**After (Fixed)**:
```sql
-- Uses configured names
WHERE root["documentType"] = "UserInfo" AND root["customPk"] = "UserInfo"
-- OR (with defaults)
WHERE root["$type"] = "UserInfo" AND root["pk"] = "UserInfo"
```

### Methods Updated

All query methods now use the configured property names:

1. ✅ `GetUserByEmailAddress` - Uses configured type and partition key names
2. ✅ `GetUserByPhoneNumber` - Uses configured type and partition key names  
3. ✅ `GetUserByDisplayName` - Uses configured type and partition key names
4. ✅ `GetUsersByDisplayName` - Uses configured type and partition key names
5. ✅ `GetUsers` - Uses configured type and partition key names

## Why This Matters

### Authentication Was Failing Because:
1. User signs in with Google using "test@example.com"
2. System tries to find existing user by email
3. Query uses wrong property names: `root["Type"]` instead of `root["documentType"]`
4. Query returns no results (user not found)
5. System creates new user instead of linking to existing one
6. **Result: Multiple accounts for same email** ❌

### Now Authentication Works Because:
1. User signs in with Google using "test@example.com"  
2. System tries to find existing user by email
3. Query uses correct property names: `root["documentType"]` and `root["customPk"]`
4. Query finds the existing user ✅
5. System adds Google provider to existing user ✅
6. **Result: Single account with multiple providers** ✅

## Configuration Flexibility

The fix supports both scenarios:

### Default Configuration
```csharp
// Uses default property names: "$type" and "pk"
CosmosConfiguration = null; // or not set
```
Generates queries: `WHERE root["$type"] = "UserInfo" AND root["pk"] = "UserInfo"`

### Custom Configuration  
```csharp
// Uses custom property names
CosmosConfiguration = new CosmosConfiguration
{
    TypeName = "documentType",
    PartitionKeyName = "/customPk"  
}
```
Generates queries: `WHERE root["documentType"] = "UserInfo" AND root["customPk"] = "UserInfo"`

## Impact

This fix resolves:

1. ✅ **User Lookup Failures**: Queries now find users with custom configurations
2. ✅ **Authentication Issues**: No more duplicate accounts created
3. ✅ **Configuration Respect**: Custom property names are properly used
4. ✅ **Provider Linking**: Multiple providers correctly link to same account

## Summary

You identified the exact root cause - the query system wasn't respecting the configured property names. The fix ensures that all Cosmos DB queries use the actual property names stored in the JSON, whether they're defaults (`$type`, `pk`) or custom values (`documentType`, `customPk`).

This was indeed a **very big issue** that would cause authentication to fail completely when using custom Cosmos configurations. Great catch!