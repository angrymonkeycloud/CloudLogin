# CloudLogin core storage architecture

The CloudLogin core is the modern storage and security model behind every API version. One shared domain/application/storage core serves the V2 compatibility façade, the V3 API, and the future V1 adapter — versions are presentation, never data. There is exactly one user database; no version ever gets its own store or a synchronization bridge to another one.

Activate it by configuring `CloudLoginWebConfiguration.Core` (see [Configuration](#configuration)). Until a deployment migrates ([docs/migration-core.md](migration-core.md)), leaving `Core` unset keeps the legacy single-container model documented in [docs/database-schema.md](database-schema.md).

## Layering

```
API DTOs (Contracts, Contracts.V3)
        │
Application services (Core/Application: CoreUserService, IdentityLinkingService,
        SessionService, DeviceAuthorizationService, SignInProfileService,
        WorkspaceAccessService, AuditLogger)
        │
Repository interfaces (Core/Abstractions)
        │
Azure adapters (Core/Azure: Cosmos repositories, Table stores)
```

Controllers call application services and repository interfaces only. No controller returns a persistence document or touches a Cosmos container directly. Authentication tickets carry only minimal identifiers — user id, session id, and the security stamp — never profile data or credentials.

The V2 compatibility adapter (`CoreCloudLoginStoreAdapter`) implements the legacy `ICloudLoginStore` surface on top of these layers, so the whole existing V2 behavior — routes, JSON names, status codes, redirects, cookies — is preserved while persistence moves.

## Storage responsibilities

| Store | Holds | Never holds |
| --- | --- | --- |
| Azure Cosmos DB (7 containers) | Everything that expires or is queried: users, credentials, workspaces, access records, sessions, login/device requests, audit events | — |
| Azure Table Storage | Permanent point-lookup records only: the `LoginIdentityKeys` index and the optional `LoginUserWorkspaceIndex` | Anything expiring |
| Azure Blob Storage | Large, non-queryable, non-expiring content: profile images, migration checkpoints and reports, legacy per-user security documents until migrated | Authentication requests, sessions, tokens, credentials, TOTP secrets, or any expiring security record |
| Azure Key Vault / Managed HSM | Production token signing keys, preferably non-exportable (see [Signing keys](#signing-keys)) | — |

### Azure Storage names

A storage account is normally shared with the other components of the same product, so everything CloudLogin creates there is prefixed with `login` — an unprefixed `IdentityKeys` sitting beside another component's tables gives nobody a clue who owns it, or whether it is safe to touch. The Cosmos database is named `Login` for the same reason, so its containers can keep their short names.

| Resource | Name | Configurable |
| --- | --- | --- |
| Table | `LoginIdentityKeys` | no — fixed |
| Table | `LoginUserWorkspaceIndex` | no — fixed |
| Blob container | `login-users` | yes — `Storage:ContainerName` |

The blob container carries a hyphen and the tables do not, because Azure's naming rules differ: **table names permit alphanumeric characters only**, so `Login-IdentityKeys` would be rejected by the service, while **blob container names permit lowercase letters, digits and hyphens**. The readable hyphenated form is used wherever it is legal. `StorageNamingTests` enforces both the prefix and the per-resource legality rules, so an illegal name fails the build rather than surfacing at runtime as a failed sign-in.

#### Renaming an existing deployment

Nothing is moved automatically — CloudLogin will not silently relocate storage it did not create.

- **Blob container**: a deployment created before this convention keeps its existing blobs by setting `Storage:ContainerName` to its old value (`users`) explicitly.
- **Tables**: the names are fixed constants, so there is no equivalent pin. A deployment that already has an `IdentityKeys` table gets a new, **empty** `LoginIdentityKeys` on first start, and the old rows are simply invisible.

That last point is the dangerous one, and it is silent. The identity index is what every sign-in resolves through, so an empty index beside a populated `Users` container means a returning user's email resolves to nothing and is treated as a new person — a duplicate account, and possibly a second bootstrap global-admin reservation. There is no built-in rebuild: the index is written incrementally by `IdentityLinkingService` as identities are added, and only the legacy migration populates it in bulk.

So for any deployment with real accounts, copy the rows before the first start on the new name (Azure Storage Explorer or `azcopy` will copy a table wholesale; the entities are self-contained and need no transformation). For a disposable environment, letting it recreate empty is fine as long as the `Users` container is empty too.

## The seven Cosmos containers

| Container | Partition key | TTL | Holds |
| --- | --- | --- | --- |
| `Users` | `/id` | none | User profile, lifecycle state, locale, timestamps, security stamp, `SchemaVersion` |
| `Credentials` | `/UserId` | `-1` | One document per credential: password hashes, passkeys, protected TOTP, external identities, temporary recovery artifacts |
| `Workspaces` | `/id` | none | Workspace profile, lifecycle, timestamps |
| `WorkspaceAccess` | `/WorkspaceId` | `-1` | Memberships (permanent, no `ttl`) and invitations (positive `ttl`) |
| `Sessions` | `/FamilyId` | `-1` | Refresh-token families and their token generations; hashes only |
| `LoginRequests` | `/id` | `-1` | One-time login handoffs and RFC 8628 device requests; hashes only |
| `AuditEvents` | `/partitionKey` | `-1` | Append-only security events, partitioned `{realm}|{subject}|{yyyyMM}` |

Container names and partition key paths are fixed (`CloudLoginCoreContainers`); provisioning happens automatically on first use (`CosmosCoreDatabase`), or up front through `ProvisionAllAsync`.

### TTL rules

Every container that can hold an expiring document is provisioned with `DefaultTimeToLive = -1`: TTL is armed at container level, but nothing expires unless the document itself says so.

- Every expiring document carries both a positive `ttl` and an absolute `ExpiresOn`.
- Non-expiring documents omit `ttl` entirely (memberships, permanent credentials).
- Cosmos counts TTL from the last modification, so every write recomputes `ttl` from `ExpiresOn` (`DocumentExpiry.Recompute`) — updating a document can never accidentally extend its absolute lifetime.
- Cosmos deletes expired documents asynchronously, so every read of an expiring document also validates `ExpiresOn` in application code (`DocumentExpiry.IsExpired`).
- There is deliberately no scheduled or background cleanup job; native TTL is the only deletion mechanism for expiring data.

### Dates

Two rules, both enforced by `DateConventionTests` rather than by review:

- **Stored instants are UTC.** `UtcDateTimeOffsetConverter` normalizes every `DateTimeOffset` on write, so the same moment written from UTC+3 and UTC-5 persists identically and range queries never depend on where the writer was. Only the display layer converts — the account and admin pages call `.ToLocalTime()`, which in Blazor WebAssembly is the viewer's own browser timezone.
- **A stored date is named `…On`.** Never `…At`, and never with a `Utc` suffix: that a value is UTC is a storage fact, not part of its name. So `ExpiresOn`, `CreatedOn`, `RevokedOn`, `LastSeenOn`, `OccurredOn` — not `ExpiresAt` or `ExpiresAtUtc`. The one allowed exception is `DateOfBirth`, a calendar date with no timezone to convert.

### Consistency boundaries

- One user's credentials share the `/UserId` partition; one workspace's access records share `/WorkspaceId`; one session family shares `/FamilyId`. Within each of those partitions, reads after writes are strongly consistent and transactional batches are available.
- Refresh rotation is a single transactional batch inside the family partition (consume old token, create new token, advance the family head), each leg guarded by the ETag read beforehand — two concurrent exchanges of the same token can never both succeed.
- Login/device request state transitions are ETag-conditional replaces on a single document: claim, approve, and consume each have exactly one winner.
- The last-owner invariant is enforced by pre-checks plus a post-write verification inside the workspace partition; the verification closes the window where two concurrent owner demotions each saw the other owner.
- The `LoginUserWorkspaceIndex` table is non-authoritative. It is maintained idempotently, failures never fail the operation, and readers confirm against `WorkspaceAccess`.

## Identity index (Azure Table Storage)

`LoginIdentityKeys` resolves normalized email addresses, phone numbers, and external `(issuer, subject)` identities to a `UserId` and a `ContactId` with a single point lookup:

- **PartitionKey** = `{identityType}-v{hashVersion}-{bucket}` — for example `Email-v1-3f`. The bucket is the first two hex characters of the identity hash, so one identity type spreads over 256 partitions, and a future hash or normalization change lands in its own partitions instead of colliding with today's rows.
- **RowKey** = the **HMAC-SHA256** of the canonical identity string (`email:{normalized}`, `phone:{normalized}`, `ext:{issuer}|{subject}`), keyed with `CloudLogin:IdentityHmacSecret`.
- **Columns** = `UserId`, `ContactId`, `IdentityType`, `SchemaVersion`, `HashVersion`, `NormalizationVersion`, `CreatedOn`. The canonical value itself is **not stored**.
- Inserts are **create-only** (`AddEntityAsync`): a collision surfaces as a conflict for the caller to handle; nothing ever silently overwrites another user's identity.
- Deletes are ETag-conditional, so a claim re-made between a read and a delete survives instead of being removed on the strength of a stale decision.
- The table also holds one-time bootstrap reservations — the first-administrator grant is an atomic create-only insert, so two racing first registrations can never both become the administrator.

### Why the row key is keyed, and why the plaintext is gone

A bare SHA-256 of `email:ada@example.com` is computable by anyone who can read the table. That made the index a confirmation oracle: run a dictionary of addresses through SHA-256, and the rows tell you exactly which of them have accounts here — without a single request to the application. HMAC removes that, because the row keys mean nothing without the secret, and resolution still costs one point read since the same secret is applied on every write and every read.

Storing `CanonicalValue` beside the hash would have defeated the change entirely, so it is not stored. Nothing needs it: every lookup arrives holding the value and re-derives the key, and what a caller actually wants back — which user, which contact — is in the columns.

### The key

There is one primary key and an optional ordered fallback array. The primary writes; fallbacks only
read old rows during a deliberate rotation.

| Form | Where it applies |
| --- | --- |
| `CloudLogin:IdentityHmacSecret` | The logical configuration key — appsettings, user secrets, any configuration provider. |
| `CloudLogin__IdentityHmacSecret` | The environment-variable spelling, and what the Aspire integration injects. Double underscores because Linux App Service and containers will not accept a colon in a variable name. |
| `CloudLogin:IdentityHmacFallbackSecrets` / `CloudLogin__IdentityHmacFallbackSecrets` | One secret setting containing a JSON array of old keys, for example `["base64-old-1","base64-old-2"]`. |

At least 32 cryptographically random bytes, base64 or hex. Startup fails with an actionable message when the value is missing, malformed, too short, or visibly not random, and the value is never logged, echoed in an error message, or returned by any API.

CloudLogin never derives a key from an application name. To rotate deliberately, deploy the new
key as `IdentityHmacSecret` and the previous key(s) in `IdentityHmacFallbackSecrets`. Every lookup
checks the primary and configured fallbacks, rejects conflicting owners, creates/confirms the
primary row, and only then conditionally removes the old row. New claims also check all fallback
locations, so an old identity cannot be claimed by a second user. Removing a fallback is a manual
operation and is safe only after every row using it has been migrated.

Required only where there is an identity index to key: database version V3 with Azure Storage configured. V1 and V2 deployments, and any host running on its own in-memory `ICloudLoginStore` (the demos, the tests), never need the setting.

#### Under Aspire

`AddCloudLogin` wires the secret automatically — an Aspire parameter holding base64 of `RandomNumberGenerator.GetBytes(32)`, marked secret and persisted:

```csharp
// Nothing to configure. The parameter is created, kept, and injected as
// CloudLogin__IdentityHmacSecret.
var login = builder.AddCloudLogin<Projects.Contoso_Login>("login");

// Or bring your own - a key vault value, or one shared with something outside the AppHost.
login.WithIdentityHmacSecret(builder.AddParameter("login-identity-hmac", secret: true));

// Optional rotation support: one secret parameter whose value is a JSON array.
login.WithIdentityHmacFallbackSecrets(
    builder.AddParameter("login-identity-hmac-fallbacks", secret: true));
```

Stability comes from Aspire's own parameter machinery rather than anything CloudLogin invents. Locally, the generated value is written to the AppHost's user secrets on first run and read back on every run afterwards. When published, the manifest carries a *description* of how to generate the secret — never the value — which the deployment resolves once per environment and then reuses across republishes, deployment slots and scaled instances, so every replica keys the index identically.

Each CloudLogin resource gets its own parameter (`{resource}-identity-hmac`), because two authorities in one AppHost are two separate identity indexes. The value is random bytes, never anything derived from a resource or deployment id.

#### Everywhere else

A deployment that does not use the Aspire integration supplies the setting itself — generate it once and put it in whatever secret store the platform offers:

```bash
openssl rand -base64 32
```

Then set `CloudLogin__IdentityHmacSecret` as an application setting (App Service, container environment, systemd unit) or `CloudLogin:IdentityHmacSecret` through any configuration provider. Treat it as permanent and back it up with the database. For rotation, add the old value to the single JSON-array fallback setting before changing the primary.

### Realms

The realm used to be part of every partition key. With the identity type and hash version there instead, realm isolation moves up to the physical names: each realm gets its own identity table **and** its own Cosmos database, both derived from one realm identity so the two can never disagree about which realm they belong to.

| Realm | Identity table | Cosmos database |
| --- | --- | --- |
| `default` (or unset) | `LoginIdentityKeys` | `Login` |
| anything else | `LoginIdentityKeys{suffix}` | `Login{suffix}` |

The suffix is `v1` followed by 16 hex characters of SHA-256 over the lower-cased realm id.

**It is hashed rather than sanitized, and that is the whole point.** Azure table names permit alphanumeric characters only, so the obvious approach — strip everything else — is not injective: `tenant-a` and `tenant_a` both reduce to `tenanta`, and two realms would silently share one identity index and resolve each other's addresses. A realm of pure punctuation reduced to nothing at all and collided with the default realm's unsuffixed table. Hashing is total (every realm id maps somewhere, no character is unrepresentable) and injective for anything anyone will configure.

Sixteen hex characters is a namespacing device, not a security boundary — a collision needs on the order of four billion realms in one storage account before it is likely. The `v1` prefix does two jobs: it lets this derivation change later without the new names colliding with rows written under the old one, and it guarantees a named realm can never produce an empty suffix, which is what makes the default realm's backward-compatible unsuffixed names safe to keep.

Realm ids are compared case- and whitespace-insensitively, matching `IsDefaultRealm`, so `Tenant-A` and `tenant-a` are one realm rather than two.

`Core.DatabaseId` left unset resolves from the realm, so one database per realm is what a deployment gets without arranging it. Naming it explicitly is allowed, but validation rejects a name inside the `Login…` namespace that the derivation owns — a hand-picked name in there is some other realm's database.

External identities are always `(realm, issuer, subject)`, never email alone. Emails and phones are normalized in exactly one place (`IdentityNormalization`) so every path produces byte-identical canonical strings.

## Contacts and credentials

Every email and phone contact on a user document carries an immutable `ContactId`, assigned once when the contact is first added and never reassigned — not when the address is re-cased, not when normalization changes, not when the person edits the display form.

Everything that points at a contact points at that id:

- A password credential is `password|{contactId}` and carries `UserId` + `ContactId`.
- An external identity carries `UserId` and an optional `LinkedContactId`, plus the provider's reported email and whether the provider verified it (display only — linking and resolution key on `(issuer, subject)`).
- An identity index row carries `UserId` + `ContactId`.

Keying those on the address itself is what used to make a corrected email address orphan its own password: the key moved while the credential stayed where it was. The contact id does not move.

An email or phone identity is only reserved once it has been **verified**. The reservation is permanent and exclusive — whoever claims `ada@example.com` owns it for every future sign-in — so claiming on an unverified value would let anyone who can type an address lock out its real owner. `ClaimIdentityAsync` refuses an unverified email or phone outright (`UnverifiedIdentityException`). External identities are exempt because the completed provider flow *is* their verification.

## Provider linking policy

- An unverified email never links anything.
- A provider returning the same verified email as an existing account requires an authenticated linking ceremony by default: the person signs in to the existing account and approves the link with recent authentication plus completed provider proof.
- Trusted-issuer automatic linking exists but defaults to disabled (`Core.IdentityLinking.AllowTrustedIssuerAutoLink` plus an explicit issuer list).
- Linking an identity already owned by another user is refused (`IdentityAlreadyLinkedException`).
- Removing a user's final usable sign-in method is refused (`FinalSignInMethodException`).
- New-user creation across Cosmos and Table Storage runs as a reservation saga: identity keys are claimed first (create-only), then the user document and credentials are written; any failure releases the keys that call claimed. Residue from a crash between compensation steps is repaired by re-running the idempotent migration/reconciliation.

## Signed-in devices

A **device is a refresh-token family**: an application signing a user in through CloudLogin creates one, and revoking it signs that device out and leaves the others alone. The account page's Security tab lists them, and `GET /api/v3/devices` returns the same data.

Each entry carries what the user agent reported — a name ("Chrome on Windows"), a broad type (Desktop / Mobile / Tablet / Unknown), browser and operating system — plus the address seen at sign-in, the address at the most recent token exchange, when the session started, and when it was last active. `IsActive` is false once the session is revoked or past its absolute expiry, and an inactive entry keeps its `RevocationReason` so someone can see *why* a device stopped — `TokenReuseDetected` being the one worth noticing.

Two deliberate limits:

- **Descriptive, never authoritative.** A user agent is client-supplied text and trivially forged. It exists so a person can recognise their own devices; nothing here is ever an input to an authorization decision.
- **A device is a token session, not a browser cookie.** Signing in to CloudLogin's own account page creates a cookie session, not a token family, so it does not appear in this list — those sign-ins are recorded in the sign-in history instead. Listing and revoking authority browser sessions would require validating cookies against a server-side session store, which CloudLogin deliberately does not do (its cookies are stateless and Data Protection-sealed). `RevokeDeviceAsync` therefore only ever reports success for something it can genuinely revoke.

`SessionService.RevokeDeviceAsync` checks ownership and answers false for an id belonging to another account — indistinguishable from one that never existed, so device ids cannot be probed.

## Signing keys

Production deployments sign tokens with an Azure Key Vault or Managed HSM key (`CloudLoginTokens:SigningKeys:KeyVaultKeyId`), created non-exportable: signatures are computed inside the vault (`CryptographyClient`), rotation is the vault's key-version rotation, and JWKS publishes the enabled versions' public coordinates.

The Cosmos `SigningKeys` fallback remains available and is the default — private keys wrapped with Data Protection, retirement through TTL — so a deployment that configures nothing still runs. A deployment whose policy requires a vault key can make the choice mandatory by setting `CloudLoginTokens:SigningKeys:RequireExplicitStoreChoice` to `true`; startup then fails until either `KeyVaultKeyId` or `AllowCosmosFallback` is set.

On a core deployment the fallback lives in its own `SigningKeys` container in the core database (created only on first use), and the V2 refresh-token surface is served from the `Sessions` container through `CoreTokenStoreAdapter` — no expiring security state remains outside the core model.

## Configuration

```csharp
builder.AddCloudLoginWeb(options =>
{
    options.Cosmos = new(builder.Configuration.GetSection("Cosmos"));
    options.AzureStorage = new(builder.Configuration.GetSection("Storage")); // required by the core

    options.Core = new CloudLoginCoreConfiguration
    {
        RealmId = "default",           // isolates identity keys and audit partitions
        DatabaseId = "Login",          // Cosmos database holding the seven containers - the default
        InvitationLifetime = TimeSpan.FromDays(14),
        AuditRetention = TimeSpan.FromDays(400),
        SessionFamilyLifetime = TimeSpan.FromDays(30),
        RefreshTokenLifetime = TimeSpan.FromDays(14)
    };
});
```

`DatabaseId` defaults to `CloudLoginCoreContainers.DefaultDatabaseId` ("Login") when omitted - only set it explicitly to use a different name.

The identity index key is configured separately from this object so it stays in a secret store: `CloudLogin:IdentityHmacSecret`, or `CloudLogin__IdentityHmacSecret` as an environment variable (see [The key](#the-key)). Under Aspire it is supplied automatically.

Two rules are enforced at startup rather than trusted:

- A non-default `RealmId` must name its own `DatabaseId`. Sharing one database across realms would put both realms' users in the same containers with nothing separating them.
- `Core.DatabaseId` must differ from `Cosmos:DatabaseId`. The V3 core database is separate from the legacy V2 database by design; pointing them at one name would leave two storage models writing into the same place.

The same section binds from configuration under `CloudLogin:Core` for Aspire-projected or appsettings-driven hosts. The API version, the deployment/authority version, and the storage `SchemaVersion` are three independent axes: `ApiVersion` selects one façade, the package version is the deployment, and `SchemaVersion` on each document only changes when the persisted JSON layout changes.

### CloudLogin creates its own database and containers

CloudLogin owns its schema. On startup it creates whatever the selected database version needs -
the `Login` database and its seven containers under V3, the legacy database and its single
container under V2 - so **no external provisioning step is required**:

```csharp
// Nothing about storage layout is configured: V3 is the default and CloudLogin builds it.
builder.AddCloudLoginWeb(options =>
{
    options.Cosmos = new(builder.Configuration.GetSection("Cosmos"));
    options.AzureStorage = new(builder.Configuration.GetSection("Storage"));
});
```

This works identically whether the app is composed by an Aspire/CoconutSharp AppHost, run with
`dotnet run` against a connection string, or published to App Service
(`CloudLoginStorageProvisioner`, registered by both the standalone and embedded hosts). Every
call is create-if-not-exists, and the container TTL settings are verified and repaired on each
start, so running it repeatedly is safe.

One case still needs the AppHost's help: creating databases and containers is a Cosmos
*control-plane* operation, which the data-plane RBAC role a managed identity usually holds does
not grant. There, provisioning must come from the deployment credentials instead - so
`WithReference(cosmos)` also **declares** the same resources, and the startup provisioner logs
and continues rather than failing when it is refused:

```csharp
login
    .WithReference(cosmos)     // declares the same schema the server would create
    .WithReference(storage)
    .WaitFor(cosmos)
    .WaitFor(storage);
```

What gets declared follows the resource's own `DatabaseVersion`, so the AppHost and the running
server never describe different storage: V3 declares the core database and its seven containers,
V2 declares the legacy database and its single container. The names come from one source of
truth - `CloudLoginCoreContainers` - on both sides.

## Document samples (before and after)

### User

Before — legacy `UserInfo` document (one container, embedded credentials and subjects):

```json
{
  "id": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "pk": "UserInfo",
  "$type": "UserInfo",
  "FirstName": "Ada",
  "LastName": "Lovelace",
  "DisplayName": "Ada Lovelace",
  "IsLocked": false,
  "IsGlobalAdmin": false,
  "CreatedOn": "2026-01-14T09:12:00Z",
  "LastSignedIn": "2026-08-20T17:03:44Z",
  "Inputs": [
    {
      "Format": "EmailAddress",
      "Input": "ada@example.com",
      "IsPrimary": true,
      "Providers": [
        { "Code": "Password", "PasswordHash": "AQAAAAIAAYagAAAAEP...", "Identifier": null },
        { "Code": "Google", "PasswordHash": null, "Identifier": "104839571023984710" }
      ]
    }
  ],
  "Country": "US",
  "Locale": "en-US"
}
```

After — `Users` container (partition key `/id`; no hash, no subject):

```json
{
  "id": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "SchemaVersion": 1,
  "FirstName": "Ada",
  "LastName": "Lovelace",
  "DisplayName": "Ada Lovelace",
  "State": "Active",
  "IsLocked": false,
  "IsGlobalAdmin": false,
  "CreatedOn": "2026-01-14T09:12:00Z",
  "UpdatedOn": "2026-08-30T10:00:00Z",
  "LastSignedIn": "2026-08-20T17:03:44Z",
  "SecurityStamp": "b2a04a4b6f2c9a114b3a9e212f43b6f1",
  "Contacts": [
    {
      "Format": "EmailAddress",
      "Value": "ada@example.com",
      "NormalizedValue": "ada@example.com",
      "IsPrimary": true,
      "IsVerified": true,
      "ContactId": "9c4f2b7e-1a63-4d58-9f21-7c0e3b5a8d42",
      "ProviderCodes": [ "Password", "Google" ]
    }
  ],
  "Country": "US",
  "Locale": "en-US"
}
```

### Credentials

After — `Credentials` container (partition key `/UserId`), one document per credential. Password:

```json
{
  "id": "password|9c4f2b7e-1a63-4d58-9f21-7c0e3b5a8d42",
  "SchemaVersion": 1,
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "Kind": "Password",
  "ContactId": "9c4f2b7e-1a63-4d58-9f21-7c0e3b5a8d42",
  "PasswordHash": "AQAAAAIAAYagAAAAEP...",
  "CreatedOn": "2026-01-14T09:12:00Z",
  "UpdatedOn": "2026-08-30T10:00:00Z"
}
```

External identity (before: `Identifier` on the input's provider entry; the subject now lives here and in `IdentityKeys`, never in the user document or any API response):

```json
{
  "id": "ext|3f79bb7b435b05321651daefd374cd21b3ce...",
  "SchemaVersion": 1,
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "Kind": "ExternalIdentity",
  "Issuer": "https://accounts.google.com",
  "Subject": "104839571023984710",
  "ProviderCode": "Google",
  "LinkedContactId": "9c4f2b7e-1a63-4d58-9f21-7c0e3b5a8d42",
  "ProviderEmail": "ada@example.com",
  "ProviderEmailIsVerified": true,
  "CreatedOn": "2026-01-14T09:12:00Z",
  "UpdatedOn": "2026-08-30T10:00:00Z"
}
```

Authenticator app (before: raw `SecretKey` in the blob security document; now Data Protection-wrapped):

```json
{
  "id": "totp",
  "SchemaVersion": 1,
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "Kind": "Totp",
  "ProtectedTotpSecret": "CfDJ8H3k...",
  "TotpIsConfirmed": true,
  "CreatedOn": "2026-03-02T10:05:00Z",
  "UpdatedOn": "2026-08-30T10:00:00Z"
}
```

Recovery artifact (temporary; always expiring):

```json
{
  "id": "recovery|password-reset|2c9f6a3e-9d5a-4a5a-8a90-1c7f6e2a4b10",
  "SchemaVersion": 1,
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "Kind": "Recovery",
  "RecoveryPurpose": "password-reset",
  "RecoverySecretHash": "5e884898da28047151d0e56f8dc6292...",
  "ExpiresOn": "2026-08-30T10:15:00Z",
  "ttl": 900
}
```

### Workspace and access

Before, workspaces had no CloudLogin-owned schema (host `ICloudLoginAccountStore`). After — `Workspaces` (partition key `/id`):

```json
{
  "id": "7d1c2e4a-0b3f-4c5d-8e9f-0a1b2c3d4e5f",
  "SchemaVersion": 1,
  "Name": "Acme",
  "State": "Active",
  "CreatedOn": "2026-05-01T08:00:00Z",
  "UpdatedOn": "2026-08-30T10:00:00Z"
}
```

`WorkspaceAccess` (partition key `/WorkspaceId`) — a membership is permanent and carries no `ttl`; multiple members may hold `Owner`:

```json
{
  "id": "member|b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "SchemaVersion": 1,
  "WorkspaceId": "7d1c2e4a-0b3f-4c5d-8e9f-0a1b2c3d4e5f",
  "Kind": "Membership",
  "State": "Active",
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "Roles": [ "Owner" ],
  "CreatedOn": "2026-05-01T08:00:00Z",
  "UpdatedOn": "2026-05-01T08:00:00Z"
}
```

An invitation always expires through TTL:

```json
{
  "id": "invite|9e21b2a0-4b3a-2f43-b6f1-4a4b6f2c9a11",
  "SchemaVersion": 1,
  "WorkspaceId": "7d1c2e4a-0b3f-4c5d-8e9f-0a1b2c3d4e5f",
  "Kind": "Invitation",
  "State": "Pending",
  "RecipientKey": "grace@example.com",
  "RecipientDisplay": "grace@example.com",
  "Roles": [ "Member" ],
  "InvitedByUserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "CreatedOn": "2026-08-30T10:00:00Z",
  "ExpiresOn": "2026-09-13T10:00:00Z",
  "ttl": 1209600
}
```

### Sessions

Before — legacy `RefreshToken` documents in the shared container (flat chain, cross-partition):

```json
{
  "id": "9a3c7e10-4455-4b66-8a11-2d9f0b6c3e77",
  "pk": "RefreshToken",
  "$type": "RefreshToken",
  "TokenHash": "5e884898da28047151d0e56f8dc6292...",
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "FamilyId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "ConsumedOn": null,
  "IsRevoked": false,
  "ttl": 2592000
}
```

After — `Sessions` container (partition key `/FamilyId`): a family head plus one document per token generation, rotated in one transactional batch. Family head:

```json
{
  "id": "f47ac10b58cc4372a5670e02b2c3d479",
  "SchemaVersion": 1,
  "FamilyId": "f47ac10b58cc4372a5670e02b2c3d479",
  "Kind": "Family",
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "SessionId": "sess_01HXYZ",
  "Audience": "portal",
  "CurrentTokenId": "8c1f27c1a2...sha256...",
  "CreatedOn": "2026-08-20T17:03:44Z",
  "IsRevoked": false,
  "RevocationReason": "None",
  "CreatedByIp": "203.0.113.7",
  "UserAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) …",
  "DeviceName": "Chrome on Windows",
  "DeviceType": "Desktop",
  "DeviceBrowser": "Chrome",
  "DeviceOperatingSystem": "Windows",
  "LastSeenOn": "2026-08-21T09:14:02Z",
  "LastSeenIp": "203.0.113.7",
  "ExpiresOn": "2026-09-19T17:03:44Z",
  "ttl": 2591990
}
```

The device fields describe the browser behind the session so the account page can list it. They come from a client-supplied user agent, so they are shown to the person and never used for an authorization decision — see [Signed-in devices](#signed-in-devices).

Token generation (the id is the SHA-256 of the raw token, so presentation is a point read; the raw value is never stored):

```json
{
  "id": "8c1f27c1a2...sha256...",
  "SchemaVersion": 1,
  "FamilyId": "f47ac10b58cc4372a5670e02b2c3d479",
  "Kind": "Token",
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "CreatedOn": "2026-08-20T17:03:44Z",
  "ConsumedOn": null,
  "ExpiresOn": "2026-09-03T17:03:44Z",
  "ttl": 1209600
}
```

### Login and device requests

Before — legacy `Request` document:

```json
{
  "id": "2c9f6a3e-9d5a-4a5a-8a90-1c7f6e2a4b10",
  "pk": "Request",
  "$type": "Request",
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "ttl": 60
}
```

After — `LoginRequests` container (partition key `/id`). The classic handoff:

```json
{
  "id": "2c9f6a3e-9d5a-4a5a-8a90-1c7f6e2a4b10",
  "SchemaVersion": 1,
  "Kind": "Login",
  "State": "Pending",
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "CreatedOn": "2026-08-30T10:00:00Z",
  "ExpiresOn": "2026-08-30T10:01:00Z",
  "ttl": 60
}
```

A device authorization request (see [docs/device-authorization.md](device-authorization.md); only hashes stored):

```json
{
  "id": "a1b2c3...sha256-of-device-code...",
  "SchemaVersion": 1,
  "Kind": "Device",
  "State": "Pending",
  "SignInProfile": "tv",
  "DeviceCodeHash": "a1b2c3...sha256-of-device-code...",
  "UserCodeHash": "d4e5f6...sha256-of-user-code...",
  "ClientId": "https://tv.example",
  "ClientDescription": "Living room TV",
  "PollIntervalSeconds": 5,
  "AttemptCount": 0,
  "CreatedOn": "2026-08-30T10:00:00Z",
  "ExpiresOn": "2026-08-30T10:10:00Z",
  "ttl": 600
}
```

### Audit events

New in the core — `AuditEvents` container (partition key `/partitionKey`), append-only, retention through TTL:

```json
{
  "id": "0c9f6a3e-9d5a-4a5a-8a90-1c7f6e2a4b10",
  "SchemaVersion": 1,
  "partitionKey": "default|b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11|202608",
  "Realm": "default",
  "EventType": "Session.ReuseDetected",
  "UserId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "OccurredOn": "2026-08-30T10:00:00Z",
  "Data": { "FamilyId": "f47ac10b58cc4372a5670e02b2c3d479" },
  "ExpiresOn": "2027-10-04T10:00:00Z",
  "ttl": 34560000
}
```

### Identity keys (Table Storage)

New in the core — `LoginIdentityKeys` table entity:

| Column | Value |
| --- | --- |
| PartitionKey | `Email-v1-3f` (identity type + hash version + hash bucket) |
| RowKey | `3f79bb7b435b05321651daefd374cd21b3ce...` (HMAC-SHA256 of `email:ada@example.com`) |
| IdentityType | `Email` |
| UserId | `b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11` |
| ContactId | `9c4f2b7e-1a63-4d58-9f21-7c0e3b5a8d42` |
| SchemaVersion | `1` |
| HashVersion | `1` |
| NormalizationVersion | `1` |
| CreatedOn | `2026-01-14T09:12:00Z` |

There is no `CanonicalValue` column and no `Realm` column. The plaintext is gone because storing it beside its keyed hash would defeat the keying; the realm is gone because it now names the table (`LoginIdentityKeys{suffix}` for a non-default realm, see [Realms](#realms)) rather than prefixing every partition.

## Related pages

- [docs/api-versioning.md](api-versioning.md) — façade configuration and V1 extension
- [docs/migration-core.md](migration-core.md) — moving a legacy deployment onto the core
- [docs/signin-profiles.md](signin-profiles.md) — named sign-in experiences
- [docs/device-authorization.md](device-authorization.md) — QR / TV sign-in
- [docs/database-schema.md](database-schema.md) — the legacy schema this model replaces
