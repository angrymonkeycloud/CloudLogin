# CloudLogin administration

CloudLogin Admin is the control plane for the whole authentication ecosystem: the people who sign in, the
applications they sign in to, the sessions and keys between them, and the trail of what changed. It is
deliberately limited to identity, authentication, sessions, applications and access control. Business data
from the applications it serves does not belong in it.

The console is the `AdminComponent` in `CloudLogin.Components`, shown at the account page's admin route. It
calls `api/v3/admin/*` on the authority through `CloudLoginClient.Admin` (`CloudLoginAdminClient`), signed in
with the same cookie as the rest of the account pages.

## Who may do what

Authorization is read from the stored account on every request, never from a token claim, so removing a role
takes effect on the next call. A locked, disabled or deleted account holds no administrative power whatever it
was granted. Not every administrator is unrestricted:

| Role | Can |
| --- | --- |
| Full administrator | Everything, including who else is an administrator. Accounts flagged `IsGlobalAdmin` count as this. |
| User administrator | Search and view accounts, disable and enable them, revoke devices, end sessions. |
| Application administrator | Applications (block, unblock, forget) and secret keys (create and revoke). |
| Security administrator | Signing keys, sessions, secret key revocation, providers and the audit trail. |
| Audit reader | Read only: the dashboard and the audit trail. |

The permission names (`applications.manage`, `secretkeys.manage`, `secretkeys.revoke`, `keys.manage` and so on)
live in `AdminPermissions`; `AdminRolePolicy` maps roles to them. Listing secret keys needs `applications.read`.
Ending an application's sessions without blocking it needs `sessions.revoke`. Each console tab, and each action
inside it, is offered only when the caller holds the permission, and the server enforces the same permission
regardless.

Guards that keep the realm from locking itself out: the last full administrator cannot be demoted or disabled,
and an administrator cannot disable their own account.

## What the console covers

- **Overview**: users, active sessions, applications, blocked applications, applications unused for 90 days,
  secret keys expiring within 30 days, failed sign-ins in the last 24 hours, the secret keys that need attention,
  and the latest security events.
- **Users**: search, status, how they sign in (local credential, authenticator app, passkeys), linked providers,
  sessions. Disable and enable (disabling ends every session and rotates the security stamp), revoke a passkey
  (also signs the account out), revoke sessions, sign out everywhere.
- **Applications**: every application that has used the authority, as it was seen. Block, unblock, end its
  sessions, forget. See below.
- **Secret keys**: the keys websites with a backend authenticate with. Create, revoke, and see expiry and last
  use. See below.
- **Sessions**: active sessions across the realm, per user and per application, revoke one, or end everything an
  application holds when it is compromised.
- **Security**: token signing keys (active, retired, expiry, rotate now) and secret keys that are expired or about
  to expire.
- **Providers**: the configured sign-in methods, kept separate from client applications, with the users linked
  to each external provider.
- **Audit**: filterable by event group, application and result.
- **Administrators**: who holds which role.

## Applications

Applications are observed, never created. A record appears the first time an application authenticates or signs
someone in, and there is nothing to register, configure or approve beforehand. There are three kinds:

- **Backend**: a website with a backend, identified by the name it gives itself (letters, digits, `.`, `_` and
  `-`, at most 64 characters, so it can never look like an origin).
- **Website**: a website without a backend, identified by its origin, `https://host[:port]` (http only on
  loopback).
- **NativeApp**: a native app, identified by its custom scheme, for example `blusky:`.

For each one the authority records the origins it was seen at (from the return URL of each sign-in), the
back-channel logout URL it announced, when it was first and last seen, the last secret key it used, its active
sessions, and whether it is dormant (not seen for 90 days).

An administrator can act on an application in the Applications tab or through `api/v3/admin/applications`:

- **Block**, with an optional reason. The application is refused from then on, at sign-in, at the token
  endpoints and at refresh, and every session it holds ends. The response gives the number of sessions ended.
- **Unblock**. It can sign people in again; the sessions the block ended stay ended.
- **End all of its sessions** without blocking it (`POST {id}/sessions/revoke`).
- **Forget**: delete the record. This is refused while the application is blocked, so forgetting never lifts a
  block. A forgotten application reappears if it signs someone in again.
- **View its sessions**.

A rejected client authentication is audited, at most once a minute per client and reason, so a guessing attack
cannot flood the trail.

## Secret keys

A secret key is the only credential. A website with a backend authenticates with HTTP Basic `name:secretKey`,
where the name is its own client id. Any active key works for any name, and several websites can share one key.
Keys come from two places:

- **Deployment keys**, from `CloudLoginTokens:SecretKeys:0`, `:1` and so on. Each must be at least 32 characters,
  which is checked at startup. An Aspire AppHost writes one. They are listed as "Declared by the deployment" with
  an id of `deployment-` and 12 hex characters, get a record the first time they are used (last used on and by),
  and can be revoked here. The revocation is stored and wins over configuration, so a redeployment that declares
  the same key again does not bring it back.
- **Admin-created keys**, from the Secret keys tab or `POST api/v3/admin/secret-keys` with `{ label, expiresOn? }`.
  Create as many as you like, with or without an expiry. The expiry must be in the future; the label is at most
  60 characters and defaults to "Secret key". A key looks like `clsk_` followed by 32 random bytes in base64url.

The secret is shown once, in the `keyValue` field of the create response. Only its SHA-256 hash and a short
prefix are stored, so it cannot be shown again; if it is lost, create a new one.

Revoking a key (`POST api/v3/admin/secret-keys/{id}/revoke`) takes effect immediately for every website using it,
and it stays refused even during a registry outage. `GET api/v3/admin/secret-keys` lists every key and
`GET api/v3/admin/secret-keys/expiring?withinDays=30` lists those that are expired or expire within that window.

Admin changes (block, unblock, forget, create, revoke) clear the authority's caches and its last-known snapshot of
the registry, so an outage right after the change cannot undo it.

## Dynamic sign-in destinations

The authority does not keep a list of application origins or return URLs. A website with a backend authenticates
with its secret key and states the return URL for each sign-in as a single-use transaction, so a preview host, a
staging slot or an Aspire-assigned port needs no entry anywhere, and an administrator manages applications rather
than hostnames.

A website with a backend calls `POST /CloudLogin/Authorize/Begin` with its key, its return URL, a `state` and a
PKCE challenge. The browser carries only the opaque `cltx:` reference, never a destination of its own choosing.
The authority redirects to the stored return URL with a code that only a key holder with the same name can
redeem, once, with the matching verifier. A website without a backend or a native app has no key: its identity is
the origin (or scheme) of its redirect URI, and the tokens it receives are valid only there.

Logout works the same way: the application opens a logout transaction and the browser is sent to the authority
with that reference. The full flows, including the consumer's correlation cookie, are in
[sign-in-and-logout.md](sign-in-and-logout.md).

The static `AllowedRedirectOrigins` and `AllowedMobileSchemes` settings remain for the legacy hand-off, which is
off unless `AllowLegacyRedirectHandoff` is set, and for logout destinations that carry no transaction. They are
never used to decide whether an application may authenticate.

## Connecting applications from the AppHost

`project.WithReference(cloudLogin)` is the whole integration. It writes the client configuration the CloudLogin
library reads: the authority, the application's name as client id and audience, its public URL, and the
AppHost's one generated secret key. The authority receives that same key once, as
`CloudLoginTokens:SecretKeys:0`, however many applications are referenced. Nothing per application is written to
the authority: no names, no return URLs, no audiences, no origins and no CORS settings.

```csharp
var login = builder.AddCloudLoginProject("login");
builder.AddProject<Projects.MelonCut>("meloncut").WithReference(login);
builder.AddProject<Projects.Spa>("spa").WithReference(login, o => o.IsPublic = true);
```

`IsPublic` marks a website without a backend or a native app. It receives no secret and needs an endpoint.

For a CloudLogin that is not in the AppHost, reference it by URL; the application receives the same
configuration keys and runs the same client code:

```csharp
var login = builder.AddCloudLogin("login", "https://login.example.com");
builder.AddProject<Projects.MelonCut>("meloncut").WithReference(login);
```

The AppHost cannot configure an authority it does not own, so an administrator creates a secret key in that
authority's Secret keys tab and supplies it as a secret parameter. By default every application referencing the
authority shares one parameter named `{authority}-secret-key`, which Aspire asks for locally and resolves from
its secret store when deployed. Pass `clientSecret:` to give one application a specific key, and `clientId:` or
`audience:` to override the resource name. Nothing at the authority is created or changed by running or
deploying the AppHost.

Each application appears in the console the first time it signs someone in, and can be blocked there.
`WithCloudLoginRedirectOrigins` is obsolete.

## Audit

Security-relevant actions are written to the existing append-only `AuditEvents` container with a timestamp,
actor, application, result and small non-sensitive metadata. Secrets, hashes, tokens and codes are never
recorded. Event groups: `Application.*` (`Blocked`, `Unblocked`, `Forgotten`), `SecretKey.*` (`Created`,
`Revoked`), `Client.*`, `Token.Issued`, `Transaction.*`, `Session.*`, `User.*`, `Login.*`, `Logout.Notified`,
`Logout.DeliveryFailed`, `Device.RevokedByAdmin`, `Admin.RolesChanged`, `Key.Rotated`.

## Deploying it

The control plane uses two containers: `Applications` (partition `/id`) and `SecretKeys` (partition `/id`). The
Aspire hosting integration declares them with the others, because the server's managed identity has no right to
create containers. A deployment that provisions Cosmos some other way must create them before the authority
starts serving: client authentication fails closed while they cannot be read (see "When the registry cannot be
read" in [sign-in-and-logout.md](sign-in-and-logout.md)). The `Scopes` container of earlier versions is no
longer used and can be deleted.

Existing global administrators are full administrators automatically. Grant other roles from the
Administrators tab. Upgrading an existing installation, including what each application must set, is covered in
[sign-in-and-logout.md](sign-in-and-logout.md).

## Not included yet

- **Enabling and disabling a provider at runtime.** Providers are declared with their credentials in
  configuration, so the Providers tab reports them and who has linked them; change them where they are declared.
- **Immediate revocation of access tokens.** They are stateless and live for at most `AccessTokenLifetime`;
  ending a session stops refresh at once.
- **A logout confirmation page for public clients without a token.** A public client signs out of the authority
  by presenting a live access token; a bare cross-site request cannot log anyone out.
