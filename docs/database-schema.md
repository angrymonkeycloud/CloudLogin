# CloudLogin database schema

This page is the authoritative reference for what CloudLogin itself reads and writes in the database and in blob storage — every container, document type, field, and a realistic JSON sample. Use it to validate a deployment's data, plan a migration, or check compatibility before upgrading. See [`docs/CosmosConfiguration.md`](CosmosConfiguration.md) for how to configure the connection, and the [README's configuration reference](../README.md#configuration-reference) for the minimal startup configuration.

Everything on this page is what the CloudLogin authority (`AngryMonkey.CloudLogin.Server`) persists on its own. It does not cover data a host application chooses to store for [workspaces, subscriptions, or billing](#not-persisted-by-cloudlogin) — that storage is entirely up to the host's own `ICloudLoginAccountStore` implementation.

## Storage backends

| Backend | Used for | Configuration |
| --- | --- | --- |
| Azure Cosmos DB | Users, pending sign-in requests, token signing keys, refresh tokens | `Cosmos` configuration section (`CosmosConfiguration`) |
| Azure Blob Storage | Per-user login history and security credentials (passkeys, authenticator app) | `Storage` configuration section (`AzureStorageConfiguration`) |

## Cosmos DB

### Single container, multiple document types

CloudLogin uses one Cosmos container for every document type (default name `Users`, configurable via `Cosmos:ContainerId`). Every document derives from `CloudLoginBaseRecord`, which adds the fields that make one container hold several unrelated shapes safely:

| Field (JSON) | Purpose |
| --- | --- |
| `id` | The document's GUID. Format controlled by `Cosmos:SaveIdMode`: `Raw` (default) writes the plain GUID; `TypePrefixed` writes `"{type}\|{guid}"`. |
| `pk` | Partition key value. Always equal to the document's type string (see below). Partition key path defaults to `/pk`, configurable via `Cosmos:PartitionKeyName`. |
| `$type` | Type discriminator. Property name defaults to `$type`, configurable via `Cosmos:TypeName`. |
| `PartitionKey`, `Discriminator`, uppercase `ID` | Legacy-schema duplicates of `pk`, `$type`, and `id`, emitted only when `Cosmos:IncludeLegacySchema` (or the older `UseLegacySchema` key) is `true`. |

Each document type has a fixed type string used for both `pk` and `$type`: `UserInfo` (overridable per deployment via `Cosmos:UserInfoPartitionKeyValue`), `Request`, `SigningKey`, `RefreshToken`.

### CloudUserInfo — user accounts

Type `UserInfo`. The persisted form of a `CloudUser` (see [`CloudLogin.Contracts`](../CloudLogin.Contracts/Models/CloudUser.cs)) — the two share the same fields; `CloudUserInfo` is what's actually written to Cosmos.

| Field | Type | Notes |
| --- | --- | --- |
| `FirstName`, `LastName`, `DisplayName` | `string?` | |
| `IsLocked` | `bool` | Blocks sign-in when `true`. |
| `IsTest` | `bool` | Created through the LoginTest shared-password flow. |
| `IsGlobalAdmin` | `bool` | Granted automatically to the first registered user. |
| `Username` | `string?` | |
| `DateOfBirth` | `DateOnly?` | |
| `CreatedOn`, `LastSignedIn` | `DateTimeOffset` | |
| `Inputs` | `CloudLoginInput[]` | Every email/phone identity linked to the account (see below). |
| `ProfilePicture` | `string?` | Active profile picture URL. |
| `IsCustomProfilePicture` | `bool` | `true` when uploaded by the user rather than sourced from a provider. |
| `ProviderProfilePicture` | `string?` | The original provider picture, kept so it can be restored. |
| `Country` | `string?` | ISO 3166-1 alpha-2 code. |
| `Locale` | `string?` | e.g. `en-US`. |

`CloudLoginInput` (embedded, not a separate document):

| Field | Type | Notes |
| --- | --- | --- |
| `Format` | `CloudLoginInputFormat` | `EmailAddress`, `PhoneNumber`, or `Other`. |
| `Input` | `string` | The email address or phone number. |
| `IsPrimary` | `bool` | |
| `PhoneNumberCountryCode`, `PhoneNumberCallingCode` | `string?` | Phone inputs only. |
| `Providers` | `CloudLoginProvider[]` | Each: `Code` (required), `PasswordHash` (nullable), `Identifier` (nullable, the provider's external subject id). |

```json
{
  "id": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "pk": "UserInfo",
  "$type": "UserInfo",
  "FirstName": "Ada",
  "LastName": "Lovelace",
  "DisplayName": "Ada Lovelace",
  "IsLocked": false,
  "IsTest": false,
  "IsGlobalAdmin": false,
  "Username": null,
  "DateOfBirth": null,
  "CreatedOn": "2026-01-14T09:12:00Z",
  "LastSignedIn": "2026-08-20T17:03:44Z",
  "Inputs": [
    {
      "Format": "EmailAddress",
      "Input": "ada@example.com",
      "IsPrimary": true,
      "PhoneNumberCountryCode": null,
      "PhoneNumberCallingCode": null,
      "Providers": [
        { "Code": "Password", "PasswordHash": "AQAAAAIAAYagAAAAEP...", "Identifier": null },
        { "Code": "Google", "PasswordHash": null, "Identifier": "104839571023984710" }
      ]
    }
  ],
  "ProfilePicture": "https://lh3.googleusercontent.com/a/AC...",
  "IsCustomProfilePicture": false,
  "ProviderProfilePicture": "https://lh3.googleusercontent.com/a/AC...",
  "Country": "US",
  "Locale": "en-US"
}
```

### CloudRequest — pending sign-in requests

Type `Request`. Backs the `GetUserByRequestId` transport-safe lookup used by cross-device/QR sign-in flows: a short-lived pairing record that resolves to a user once sign-in completes.

| Field | Type | Notes |
| --- | --- | --- |
| `UserId` | `Guid?` | Set once the request resolves to a signed-in user. |
| `ttl` | `int` | Cosmos TTL in seconds. Default `60`. |

```json
{
  "id": "2c9f6a3e-9d5a-4a5a-8a90-1c7f6e2a4b10",
  "pk": "Request",
  "$type": "Request",
  "UserId": null,
  "ttl": 60
}
```

### CloudLoginSigningKey — token signing keys

Type `SigningKey`. An ES256 key pair used to sign and verify access tokens. The private key is wrapped with ASP.NET Data Protection before it reaches the database — a database disclosure alone does not let an attacker mint tokens.

| Field | Type | Notes |
| --- | --- | --- |
| `KeyId` | `string` | JWK `kid`, published in the token header. |
| `ProtectedPrivateKey` | `string` | Data Protection-wrapped PKCS#8 private key, base64. |
| `PublicX`, `PublicY` | `string` | Base64url EC public key coordinates (JWK `x`/`y`). |
| `CreatedOn` | `DateTimeOffset` | |
| `SigningExpiresOn` | `DateTimeOffset` | After this, the key still verifies but no longer signs new tokens. |
| `PublishExpiresOn` | `DateTimeOffset` | After this, the key leaves JWKS entirely. |
| `ttl` | `int` | Cosmos TTL in seconds. Default 90 days, set from `PublishExpiresOn`. |

```json
{
  "id": "6b2e9f0a-1234-4a3a-9b7e-8f2a6c1d0e55",
  "pk": "SigningKey",
  "$type": "SigningKey",
  "KeyId": "2026-01-a1b2c3",
  "ProtectedPrivateKey": "CfDJ8...base64...",
  "PublicX": "MKBCTNIcKUSDii11ySs3526iDZ8AiTo7Tu6KPAqv7D4",
  "PublicY": "4Etl6SRW2YiLUrN5vfvVHuhp7x8PxltmWWlbbM4IFyM",
  "CreatedOn": "2026-01-01T00:00:00Z",
  "SigningExpiresOn": "2026-04-01T00:00:00Z",
  "PublishExpiresOn": "2026-07-01T00:00:00Z",
  "ttl": 7776000
}
```

### CloudLoginRefreshToken — refresh token chains

Type `RefreshToken`. Only a hash of the token is stored, never the raw value. Tokens rotate on every use and form a chain (`FamilyId`); reusing an already-consumed token revokes the whole chain.

| Field | Type | Notes |
| --- | --- | --- |
| `TokenHash` | `string` | SHA-256 of the raw token, base64url. |
| `UserId` | `Guid` | |
| `FamilyId` | `string` | Shared by every token in one rotation chain. |
| `SessionId` | `string` | Surfaces as the token's `sid` claim. |
| `Audience` | `string?` | |
| `Scope` | `string?` | |
| `CreatedOn`, `ExpiresOn` | `DateTimeOffset` | |
| `ConsumedOn` | `DateTimeOffset?` | Set when exchanged; a second exchange means replay. |
| `IsRevoked` | `bool` | |
| `CreatedByIp`, `UserAgent` | `string?` | Informational only, never used for authorization. |
| `ttl` | `int` | Cosmos TTL in seconds. Default 30 days. |

```json
{
  "id": "9a3c7e10-4455-4b66-8a11-2d9f0b6c3e77",
  "pk": "RefreshToken",
  "$type": "RefreshToken",
  "TokenHash": "5e884898da28047151d0e56f8dc6292...",
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "FamilyId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "SessionId": "sess_01HXYZ",
  "Audience": "https://app.example.com",
  "Scope": "openid profile",
  "CreatedOn": "2026-08-20T17:03:44Z",
  "ExpiresOn": "2026-09-19T17:03:44Z",
  "ConsumedOn": null,
  "IsRevoked": false,
  "CreatedByIp": "203.0.113.7",
  "UserAgent": "Mozilla/5.0 ...",
  "ttl": 2592000
}
```

## Azure Blob Storage

Two per-user JSON documents, stored as blobs rather than Cosmos records so an active account's growing history or credential set never bloats the user document read on every request. Container name configurable via `Storage:ContainerName` (default `users`). Blob paths:

- `security/{userId}/login-history.json`
- `security/{userId}/credentials.json`

### CloudLoginHistoryDocument — sign-in history

```json
{
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "Entries": [
    {
      "Id": "d1e2f3a4-5566-4b77-8c88-9d0e1f2a3b4c",
      "SignedInOn": "2026-08-20T17:03:44Z",
      "Provider": "Google",
      "IpAddress": "203.0.113.7",
      "UserAgent": "Mozilla/5.0 ...",
      "Device": "Chrome on Windows",
      "Latitude": 40.7128,
      "Longitude": -74.006
    }
  ]
}
```

Coordinates, when present, are stored exactly as the client reported them — CloudLogin performs no geocoding lookup.

### CloudLoginUserSecurityDocument — passkeys and authenticator app

Kept separate from the user record so secret material (TOTP keys, passkey public keys) is never part of the `CloudUser` object returned to the browser.

```json
{
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "Passkeys": [
    {
      "CredentialId": "AbCdEf0123...",
      "PublicKey": "pQECAyYgASFYIL...",
      "SignCount": 12,
      "Name": "MacBook Touch ID",
      "AaGuid": "08987058-cadc-4b81-b6e1-30de50dcbe96",
      "Transports": ["internal", "hybrid"],
      "IsBackedUp": true,
      "CreatedOn": "2026-03-02T10:00:00Z",
      "LastUsedOn": "2026-08-20T17:03:44Z"
    }
  ],
  "Authenticator": {
    "SecretKey": "JBSWY3DPEHPK3PXP",
    "EnrolledOn": "2026-03-02T10:05:00Z",
    "IsConfirmed": true
  }
}
```

## Not persisted by CloudLogin core

`CloudWorkspace`, `CloudWorkspaceMember`, `CloudWorkspaceInvitation`, `CloudSubscription`, and `CloudBillingProfile` are Contracts-layer shapes only — CloudLogin defines what they look like over the wire, but does not define or own their storage. Persistence is entirely delegated to the host application's `ICloudLoginAccountStore` implementation; the in-memory store CloudLogin ships (`InMemoryCloudLoginAccountStore`) is non-persistent and intended for demos and tests only. Do not assume a fixed database schema exists for these types — a given deployment's schema for them depends entirely on how its `ICloudLoginAccountStore` is implemented.

## Related pages

- [README — Configuration reference](../README.md#configuration-reference)
- [`docs/CosmosConfiguration.md`](CosmosConfiguration.md)
- [`docs/account-registry.md`](account-registry.md)
- [`docs/identity-and-tokens.md`](identity-and-tokens.md)
