# CloudLogin API versioning

CloudLogin has **two independent version axes**:

| Axis | Setting | Values | Default | Chooses |
| --- | --- | --- | --- | --- |
| API | `ApiVersion` | `V1`, `V2`, `V3` | `V3` | What an integration sees over the wire |
| Database | `DatabaseVersion` | `V2`, `V3` | `V3` | What is written to storage |

Neither derives from the other: any API version runs on any database version. Configure nothing and you get V3 on both.

There is no database V1 — V1 is an API contract only, so storage numbering starts at the schema CloudLogin shipped with (V2). The per-document `SchemaVersion` is a third, finer-grained thing again: it changes only when a stored document's JSON layout changes.

## API version

```csharp
builder.AddCloudLoginWeb(options =>
{
    options.ApiVersion = CloudLoginApiVersion.V2;   // default: V3
});
```

Or in configuration: `CloudLogin:ApiVersion = V2`. The configured value must be `V1`, `V2`, or `V3`; startup fails otherwise.

**What the API version gates is the integration API surface.** A request to an integration endpoint belonging to another version answers `404`, exactly as if it did not exist — for example `/CloudLogin/Service/*` (V2's service channel) versus `/api/v3/service/*` (V3's).

**What it never gates is the authority's own UI and the authentication flow.** The login page, account page, admin UI, provider redirects, request handoff and token endpoints (`/api/Providers`, `/CloudLogin/User/*`, `/CloudLogin/Security/*`, `/CloudLogin/Account/*`, `/CloudLogin/Request/*`, `/CloudLogin/Token/*`) are version-neutral: they are how CloudLogin's own site works, not an integration contract, and they answer regardless of the selected API version. Gating them would take the product's own sign-in offline whenever a deployment selected a different façade for its integrations.

### Routing: the selected version owns unversioned routes

The selected façade answers both its explicit versioned paths and its supported unversioned aliases (V3: `/api/v3/users/me` and `/api/users/me`). Other façades' versioned paths are hidden.

## Database version

```csharp
builder.AddCloudLoginWeb(options =>
{
    options.DatabaseVersion = CloudLoginDatabaseVersion.V2;   // default: V3
});
```

Or in configuration: `CloudLogin:DatabaseVersion = V2`.

- **V3 (default)** — the seven-container model in [architecture-core.md](architecture-core.md). No legacy compatibility. CloudLogin creates the database (`Login`) and its containers itself on startup; `Core` holds optional tuning and needs no configuration at all. Requires the Cosmos and Storage sections (the Table Storage `LoginIdentityKeys` index is what every sign-in resolves through). The key that index is hashed with comes from `CloudLogin:IdentityHmacSecret` (or `CloudLogin__IdentityHmacSecret`), and startup fails without it — the Aspire integration supplies one automatically. Deliberate rotation uses the one JSON-array `IdentityHmacFallbackSecrets` setting (see [The key](architecture-core.md#the-key)).
- **V2** — the existing single mixed container, with the legacy schema knobs on `CosmosConfiguration` (`IncludeLegacySchema`, `SaveIdMode`, `UserInfoPartitionKeyValue`, `JsonCompatibilityMode`) available. Requires `Cosmos:DatabaseId` and `Cosmos:ContainerId` to name the existing database — there is no default, since guessing could point at the wrong data.

The boundary between them is enforced rather than silently ignored: setting a legacy schema knob under V3 fails startup (they would do nothing), and setting `Core` under V2 fails startup (it configures a model V2 does not use). Both errors name the setting and the version to change.

A host that configures no Cosmos account at all — the demos, tests, an in-memory harness — keeps whatever `ICloudLoginStore` it registered; V3 being the default never forces an Azure account on it.

## V2 — the current working API

The V2 façade is the existing API surface, unchanged: every route, JSON property name, status code, redirect, and cookie behaves as before. What changed is underneath — when `CloudLoginWebConfiguration.Core` is configured, `CoreCloudLoginStoreAdapter` implements the legacy store surface over the seven-container core, so V2 and V3 read and write the same data.

Compatibility does not extend to secrets: password hashes, TOTP secrets, provider subjects, and raw tokens are never preserved in transport or exposed for compatibility's sake. The V2 wire shapes are pinned by `V2ContractSnapshotTests`.

Concretely, `CloudLoginTransportSecurity.ForTransport` nulls two fields on every user leaving the process — `PasswordHash` and `Identifier`. Both are credentials rather than profile data: the identifier is the provider's stable subject for a person, the value the identity index is keyed on and a correlator for the same human across services. The property stays in the JSON so the shape is unchanged; its value does not. A V2 integration that was reading provider subjects out of a user response was reading a credential, and has to stop.

Example V2 response (`GET /CloudLogin/User/CurrentUser`), unchanged:

```json
{
  "ID": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "FirstName": "Ada",
  "LastName": "Lovelace",
  "DisplayName": "Ada Lovelace",
  "IsLocked": false,
  "IsTest": false,
  "IsGlobalAdmin": false,
  "CreatedOn": "2026-01-14T09:12:00Z",
  "LastSignedIn": "2026-08-20T17:03:44Z",
  "IsCustomProfilePicture": false,
  "Inputs": [
    {
      "Format": "EmailAddress",
      "Input": "ada@example.com",
      "IsPrimary": true,
      "Providers": [ { "Code": "Password" }, { "Code": "Google" } ]
    }
  ]
}
```

## V3 — the modern API

V3 lives under `/api/v3` and speaks only explicit request/response DTOs (`AngryMonkey.CloudLogin.V3`). It requires the core storage model; on a deployment without `Core`, V3 endpoints answer `501` with an explanation.

Views are least-privilege by design — self profile, public summary, administrator view, and service view are four different DTOs, and no V3 response ever contains hashes, TOTP secrets, token values, provider subjects, signing material, ETags, or storage partition keys (`V3SerializationTests` enforces this by reflection and by serialized output). There are no anonymous user list or detail endpoints in V3.

| Area | Endpoints |
| --- | --- |
| Self | `GET /api/v3/users/me`, `PATCH /api/v3/users/me` |
| Discovery | `POST /api/v3/users/discovery` (rate limited, `no-store`, minimal reply) |
| Public summary | `GET /api/v3/users/{id}/summary` (authenticated) |
| Administration | `GET /api/v3/users`, `GET /api/v3/users/{id}` (global administrators) |
| Service-to-service | `GET /api/v3/service/users/{id}` (ServiceKey scheme only) |
| Workspaces | `GET|POST /api/v3/workspaces`, members, roles, invitations, delete |
| Sign-in profiles | `GET /api/v3/signin-profile` |
| Device authorization | `POST /api/v3/device/authorize`, `POST /api/v3/device/token`, `GET /api/v3/device/pending`, `POST /api/v3/device/approve`, `POST /api/v3/device/deny` |
| Signed-in devices | `GET /api/v3/devices`, `DELETE /api/v3/devices/{deviceId}`, `DELETE /api/v3/devices` (signs every *other* device out) — always the caller's own, see [architecture-core.md](architecture-core.md#signed-in-devices) |

Example — the same user through V3 (`GET /api/v3/users/me`):

```json
{
  "userId": "b6f1b2a0-2f43-4b3a-9e21-4a4b6f2c9a11",
  "firstName": "Ada",
  "lastName": "Lovelace",
  "displayName": "Ada Lovelace",
  "country": "US",
  "locale": "en-US",
  "createdOn": "2026-01-14T09:12:00+00:00",
  "lastSignedIn": "2026-08-20T17:03:44+00:00",
  "contacts": [
    {
      "format": "EmailAddress",
      "value": "ada@example.com",
      "isPrimary": true,
      "isVerified": true,
      "providers": [ "Password", "Google" ]
    }
  ]
}
```

Workspace member management errors are structured: demoting or removing the final active owner answers `409` with a `Last owner protection` problem document; a lost concurrency race answers `409 Concurrent change`.

## V1 — the legacy contract (not yet supplied)

The V1 contract will be provided later. Today the codebase contains exactly three things for it, and deliberately nothing more:

1. **Adapter interface** — `ICloudLoginV1Adapter` (`CloudLogin.Server/Versioning/V1/`). It defines where V1 plugs in, not what V1 looks like.
2. **Registration point** — `services.AddCloudLoginV1<TAdapter>()`.
3. **Contract-test location** — `CloudLogin.Tests/V1/V1ContractTests.cs`.

Selecting V1 without a registered adapter fails startup with `CloudLoginV1NotImplementedException` and an actionable message. V1 must never be silently stubbed or invented.

### Extending when the contract arrives

1. Obtain the real V1 contract: captured requests/responses, routes, status codes, redirect behavior, and cookie names from the system of record. Do not reconstruct it from memory.
2. Extend `ICloudLoginV1Adapter` with the operations the contract requires, translating each one onto the existing application services (`CoreUserService`, `SessionService`, `WorkspaceAccessService`, ...). The adapter maps shapes; it must not add storage. V1 uses the same core as V2 and V3 — never a separate user database and never bidirectional synchronization.
3. Add the V1 controllers in a `V1` area, each gated with `[ApiVersionGate(CloudLoginApiVersion.V1)]`.
4. Pin the contract in `CloudLogin.Tests/V1/V1ContractTests.cs` with snapshot tests built from the captured contract, mirroring `V2ContractSnapshotTests`.
5. Do not carry forward password hash formats, secrets, private provider identifiers, or unauthenticated data exposure merely because V1 had them; translate to the core's security model and document each deliberate difference.
6. Register with `AddCloudLoginV1<YourAdapter>()` and select `ApiVersion = CloudLoginApiVersion.V1` for that deployment.

## Related pages

- [docs/architecture-core.md](architecture-core.md)
- [docs/migration-core.md](migration-core.md)
