# Cloud Login Database Integration Guide

## Main configuration, inside the CloudLoginConfiguration

Cloud login can work with and without database connection, without a database, all users will be treated as guests and will not have accounts on your website.

To add a database you should add the following code:

```csharp
Cosmos = new CosmosDatabase(builder.Configuration.GetSection("Cosmos")),
```

## Configuration inside the app secret:

For the code added above, you should add the configuration of it inside the secret:

```csharp
"Cosmos": {
    "ConnectionString": "<Connection String Here>",
    "DatabaseId": "<Database ID>",
    "ContainerId": "<Container ID>"
}
```