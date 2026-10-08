# Sign-in, logout and sessions

This is the reference for how an application signs a person in and out through CloudLogin, how sessions end
everywhere, and what an operator must do to run it. It describes behavior that is implemented and covered by
tests; the last section lists what has not been verified against live infrastructure.

The model in one sentence: **the authority trusts secret keys and PKCE, never addresses.** A website with a
backend proves itself with a secret key. A website without a backend or a native app is identified by the origin
(or scheme) its tokens go back to, and those tokens are valid only there. CORS, `Origin`, `Referer` and hostname
lists are not used to decide whether an application may authenticate. Browser CSRF protections stay, and they are
separate from application identity. Nothing about an application is registered in advance: it appears in the
admin console the first time it signs someone in (see [admin-portal.md](admin-portal.md)).

## Websites with a backend

A website with a backend holds a secret key and gives itself a name (its client id). It signs people in with
Authorization Code and PKCE.

1. `GET /auth/login` on the application. It makes a random `state` and a PKCE verifier, then calls
   `POST /CloudLogin/Authorize/Begin` with HTTP Basic `name:secretKey` and
   `{ returnUrl, state, codeChallenge, codeChallengeMethod: "S256", backChannelLogoutUri }`. The return URL is its
   own callback, built from `CloudLogin:PublicUrl`, never from the `Host` header. The client library sends
   `{PublicUrl}/auth/backchannel-logout` as `backChannelLogoutUri`.
2. The authority checks the key and validates the return URL: any https URL (http only to loopback), with no
   credentials or fragment. Nothing is registered, because the key is what is trusted and the code still needs
   the key and the PKCE verifier. `backChannelLogoutUri` is optional and must be on the same origin as the return
   URL; it is recorded and used for back-channel logout notifications. The authority stores a single-use
   transaction for 15 minutes against the name and returns a `cltx:` reference.
3. The application stores `{state, verifier, returnUrl}` in an HttpOnly, SameSite=Lax correlation cookie named
   `<CookieName>.tx.<hash of state>`, protected with Data Protection and limited to
   `LoginTransactionLifetime` (default 10 minutes), then sends the browser to the authority with the reference.
4. After sign-in the authority redirects to the stored return URL with `code` and `state`.
5. `GET /auth/callback` on the application requires the correlation cookie that matches `state`. A callback with
   no cookie, a different browser's cookie, an expired or already used cookie, or a tampered `state` is refused
   before anything is redeemed. This is what stops login CSRF and cross-browser callback injection.
6. The application redeems the code at `POST /CloudLogin/Token/Code` with Basic auth, the verifier and the
   return URL. The code is bound to the client name, the return URL, the transaction and the PKCE challenge. A
   wrong verifier or return URL burns the code; a different name is refused and does not burn it; a code works
   once. A backend may name the audience it needs.

The old hand-off (a plain `requestId` in the redirect, no PKCE, no state) is disabled. It is available only
while `AllowLegacyRedirectHandoff` is `true` and the destination is in the static allowlist. Nothing falls back
to it silently.

## Websites without a backend and native apps

A public client has no secret and nothing is registered for it. It uses the browser flow directly:

- `GET /CloudLogin/Authorize?redirect_uri=...&state=...&code_challenge=...&code_challenge_method=S256`.
  `CloudLoginPkce.BuildAuthorizeUrl(authority, redirectUri, state, verifier)` builds it. A `client_id` is not
  needed and is ignored.
- Its identity is the origin of the redirect URI: `https://host[:port]` for a website (http only on loopback),
  or the custom scheme for a native app, for example `myapp:`. Custom schemes are allowed only for native apps.
- PKCE with S256 is mandatory. The code is redeemed at `POST /CloudLogin/Token/Code` with `client_id` set to the
  origin (or the scheme, for a native app) and `code_verifier`, no secret.
- The tokens are valid only for that origin: the audience is fixed to it, and asking for another audience fails
  with `invalid_target`.
- A website is issued no refresh token. A native app is issued one, bound to its scheme, rotated on use, with
  reuse detection. It refreshes with `client_id` set to its scheme.
- A public client cannot call the endpoints that need a key (`Authorize/Begin`, `Token/SessionStatus`,
  `Token/Exchange`).

## Client authentication

Every endpoint that identifies an application goes through one authenticator.

| Method | Header | Notes |
| --- | --- | --- |
| Secret key | `Authorization: Basic base64(name:secretKey)` | Websites with a backend. Any active key works for any name. |
| None | `client_id` only | Websites without a backend and native apps. The id is the origin or scheme. |

The name must match `[A-Za-z0-9._-]{1,64}`, so it can never look like an origin. Several websites can share one
key, and a key can be revoked at any time in the admin console, which stops it for every website using it. Keys
come from the deployment (`CloudLoginTokens:SecretKeys:N`) or are created in the admin console; only their
SHA-256 hashes are compared. See [admin-portal.md](admin-portal.md).

`/.well-known/openid-configuration` lists the authorization, token, refresh, revocation, exchange, transaction and
session-status endpoints, `authorization_code`, `refresh_token`, token exchange, `S256`, `client_secret_basic`,
`none`, and back-channel logout.

## Refresh

A refresh token belongs to the client that received it. Every refresh re-evaluates current policy:

- A website with a backend must present a valid key and the same name the token was issued to. Any active key
  works, so rotating keys does not break refresh. A native app presents its scheme as `client_id`.
- A blocked application is refused. Its token family is kept, but blocking already ends its sessions.
- A user who is locked or no longer exists cannot refresh.
- Rotation and reuse detection are unchanged.

**No refresh token exists without a client.** Every refresh token names the client it was issued to, and that
client must authenticate to use it. `POST /CloudLogin/Token/Session` (an access token for a person with an
authority cookie) therefore issues an access token only. A refresh token that names no client, such as one issued
before this rule, is refused at refresh and the person signs in again. The single exception is
`AllowUnboundRefreshTokens` on the token options: explicit, off by default, and only for a deployment that still
has a caller depending on the old behavior.

## Token exchange

`POST /CloudLogin/Token/Exchange` is for websites with a backend only. The subject token must have been issued to
the calling backend's own name. Any backend audience may be requested unless that application is blocked. The
result is an access token with an `act` claim naming the caller, and no refresh token.

## When the registry cannot be read

Client authentication reads the `SecretKeys` and `Applications` containers: revoked keys and blocked applications
live there. If they cannot be read, the authority uses the last snapshot it read successfully, for up to
`RegistryStaleTolerance` (default 15 minutes). Beyond that, or with no snapshot at all, such as a process that
starts during an outage, the request is refused (`RegistryUnavailable`, logged as a failure and not as an unknown
client). An outage can stop sign-ins; it cannot lift a restriction. Admin changes (block, revoke) clear the
caches and the snapshot, so an outage right after the change cannot undo it.

`AllowDeploymentKeysWithoutRegistry` lets deployment keys work with no registry at all. It is a legacy
compatibility switch, explicit and off by default, because it lets an outage bypass a revoked key or a blocked
application.

## Revoking a session

`POST /CloudLogin/Token/Revoke` with a `session_id` ends it only for its owner: the signed-in cookie of the same
user, or a bearer token for that same session. Anyone else, including another signed-in user, gets the same
`401` whether or not the session exists. Administrators revoke through the admin API, which has its own
permission check and audit entry. Revoking a browser session also ends every application session that shares
its session id.

## Logout

1. The application's `/auth/logout?returnUrl=/path` accepts only same-origin requests. It ends the local cookie,
   records the session as revoked locally, then calls `POST /CloudLogin/Authorize/Logout` with its key and
   a post-logout URL on its own origin, and redirects the browser to `/CloudLogin/Logout?referer=cltx:...`.
2. The authority spends the reference. A logout is authorized by a live logout transaction opened by an
   authenticated application: it must exist, be unexpired, be unused, and be of the logout kind. The destination is
   the transaction's URL. For a website with a backend that URL had to be https (http only on loopback); for a
   public client it had to be on its own origin.
3. A request that carries no such transaction is authorized only if the browser says it comes from the authority's
   own pages (`Sec-Fetch-Site` of `same-origin` or `none`; for a browser that sends none, a `Referer` on another
   site counts as cross-site). A fabricated, expired, replayed or wrong-kind reference authorizes nothing: the
   request lands on `/` and the session is untouched. Another site therefore cannot log someone out.

The destination can only ever be the transaction's URL, a URL in the static allowlist on a same-origin request, or
`/`. An unusable destination never prevents a logout that was otherwise authorized and never authorizes one.

If the authority cannot be reached or refuses the transaction, the local session still ends and the browser
lands on a safe local page. An application at a new hostname needs nothing registered to log out: its key
authorized the transaction. A public client opens the logout transaction by presenting a live access token issued
to it for an active session (`Authorization: Bearer`, plus its origin or scheme as `clientId` in the body), and
the destination must be on its own origin. A token from another client, a forged token, or one whose session has
ended is refused.

## Global sign-out (back-channel logout)

When a session ends at the authority, every application holding that session is told.

| Trigger | Notification |
| --- | --- |
| Sign out at the authority, or from any application | The session, for every application sharing it |
| Admin revokes a device or session | The session |
| Sign out everywhere, admin revoke-all, user disabled | Each session, then the user (subject) |
| Admin blocks an application or ends its sessions | The session, for that application only |
| Refresh reuse detected | The family's session, for that application |

The authority POSTs `logout_token` (form encoded) to the back-channel logout URL the application announced at
sign-in. The token is a
`logout+jwt` signed with the authority's ES256 key with `iss`, `aud` (the client id), `sub`, `sid`, `jti`, `iat`,
`exp` (at most two minutes), the `events` claim, and `revoked_before_ms` (the authority's clock when it
revoked), and no `nonce`. Delivery makes up to four attempts with
increasing delays and a five second timeout, writes `Logout.Notified` or `Logout.DeliveryFailed` to the audit
trail (with the attempt count) and counts successes and failures on `BackChannelLogoutService`. Repeat
notifications for the same client and session within 30 seconds are merged; user-level notifications are never
merged.

The application (`POST /auth/backchannel-logout`) accepts a token only when the signature verifies against the
authority's published keys, the issuer, audience and type are right, it is unexpired with sane timestamps, it
carries the logout event, and it names a `sid` or `sub`. Anything invalid gets `400` and changes nothing. A `sid`
ends that session. A `sub` ends that person's sessions that the authority issued at or before
`revoked_before_ms`; the application's cookie records the authority's issue time (`issued_at_ms` in the token
response), so only the authority's clock is compared and a later sign-in is never caught by an older notification.

Applying a notification is idempotent, which is what makes processing safe across instances. A session id stays
revoked, and a subject is revoked up to a timestamp that comes from the token, so two instances applying the same
token at once, or a replay of it later, end up with the same state and cannot sign out a later session. The
`jti` is recorded as processed only after the revocation was stored. If the write fails the application answers
`503`, the token stays unprocessed, and the authority's retry (or the next delivery) applies it; a repeat of a
token that was applied gets `200`. `IDistributedCache` has no atomic set-if-absent, so correctness rests on
idempotence rather than on a once-only marker; the marker only avoids repeating work. One narrow case remains: the
subject timestamp is kept with a read, write and read-back loop, so two different notifications for the same person
racing within milliseconds could leave the older timestamp. Session-level notifications, which carry no timestamp,
are sent for every known session first and do not have this property.

Application cookies carry the authority session id and the issue time, and are checked against a revocation
store on every request. That store is `IDistributedCache`, so **every instance of an application must share one
cache** (Redis, SQL or similar) for a notification delivered to one instance to end the session on all of them.
The default in-memory cache is correct for a single instance only.

If a notification is lost (the application was down, the process holding the queue died), a backstop can catch
it: at most once per `CloudLogin:SessionRevalidationInterval` (default five minutes) per session, the application
asks `POST /CloudLogin/Token/SessionStatus` whether the session is still active. That interval is the normal
recheck cadence, **not a guaranteed maximum**. A check that fails (the authority is unreachable) keeps the session
and is not tried again until the next interval, so while the authority is down a missed notification is not
corrected. There is no fixed upper bound: the session lasts until a check succeeds or the application cookie
expires, and the cookie slides (`SessionDuration`, default 8 hours, is an idle timeout), so a person who keeps
using the application keeps it. This is a deliberate availability choice: an authority outage does not sign
everyone out. Treat the back-channel notification as the revocation mechanism and the recheck as a best-effort
repair that works whenever the authority can be reached.

**Access tokens.** Access tokens are stateless and cannot be revoked; they live for at most
`AccessTokenLifetime` (default 10 minutes, validated to be at most one hour). Ending a session kills the refresh
token immediately, so no new access token can be minted. A downstream service that validates tokens offline
accepts an already issued token until it expires. A service that needs an immediate cut-off asks the authority
about the session id in the token.

## What `WithReference` writes

`project.WithReference(login)` writes to the application: `LoginUrl`, `CloudLogin:Authority`,
`CloudLogin:ClientId` and `CloudLogin:Audience` (both the resource name), `CloudLogin:PublicUrl` (its own
endpoint, resolved at run or publish time; none if it has no endpoint) and `CloudLogin:ClientSecret`, which is the
AppHost's one generated secret parameter, `{login}-secret-key` (48 characters). The authority receives
`CloudLoginTokens:SecretKeys:0` set to that same parameter, written once however many applications are
referenced. Nothing per application is written to the authority: no names, no return URLs, no audiences, and
nothing about origins or CORS.

```csharp
project.WithReference(login);                       // website with a backend: the shared secret key
project.WithReference(login, o => o.IsPublic = true); // website without a backend, or a native app
```

`IsPublic` means no secret anywhere. A public application needs an endpoint to return to; without one the model
fails with a `DistributedApplicationException` saying why. An application with no endpoint that is not public
publishes no address, which suits workers. An endpoint added after `WithReference` still counts.

For an authority outside the AppHost, `builder.AddCloudLogin("login", "https://login.example.com")` and then
`web.WithReference(external)` gives the application the same configuration keys, so it runs the same code
locally and when deployed. The secret defaults to one shared secret parameter named `{authority}-secret-key`,
which is not generated: the administrator supplies a key created in that authority's admin console, and every
website referencing the authority shares it. Pass `clientSecret:` to use a specific key for one application;
`clientId:` and `audience:` are optional.

`WithServiceAccess` (the backend service channel, `CloudLogin:ServiceKeys`) is unrelated and unchanged.

## Configuration reference

| Key | Where | Meaning |
| --- | --- | --- |
| `CloudLogin:Authority` | application | Authority base URL. |
| `CloudLogin:ClientId`, `Audience` | application | The name the application gives itself, and its token audience. |
| `CloudLogin:ClientSecret` | application | The secret key. Not set for a public client. |
| `CloudLogin:PublicUrl` | application | The application's own public URL; the callback and back-channel logout URL are built from it. Required except on loopback. |
| `CloudLogin:SessionRevalidationInterval` | application | Backstop interval; `00:00:00` turns it off. |
| `CloudLogin:LoginTransactionLifetime` | application | Correlation cookie lifetime, default 10 minutes. |
| `RequireSessionBinding` | application, code (`CloudLoginServerConfiguration`) | Reject cookies with no authority session (default true). |
| `AllowLegacyRedirectHandoff` | authority, code (`CloudLoginWebConfiguration`) | Re-enables the plain request id hand-off. Default false. |
| `CloudLoginTokens:SecretKeys:0`, `:1`, ... | authority | Deployment secret keys, each at least 32 characters, validated at startup. |
| `CloudLoginTokens:RegistryStaleTolerance` | authority | How long the last snapshot of keys and applications is used while the registry is unreadable. Default 15 minutes. |
| `CloudLoginTokens:AllowDeploymentKeysWithoutRegistry` | authority | Lets deployment keys authenticate with no registry at all. Default false; legacy only. |
| `CloudLoginTokens:AllowUnboundRefreshTokens` | authority | Issues and accepts refresh tokens that name no client. Default false; legacy only. |

## Upgrading

- Existing `CloudLoginTokens:ServiceClients:*` secrets stop working. Either redeploy so the AppHost writes
  `CloudLoginTokens:SecretKeys:0`, or put each existing client secret into `CloudLoginTokens:SecretKeys:N` (each
  at least 32 characters), or create keys in the admin console and give them to the websites as
  `CloudLogin:ClientSecret`.
- Provision the `SecretKeys` container (partition `/id`) before the authority starts serving: while it cannot be
  read, client authentication is refused (see "When the registry cannot be read"). The Aspire hosting declares
  it. The `Scopes` container is no longer used and can be deleted.
- Public clients no longer need registration. Their old client ids are replaced by their origin (or scheme, for a
  native app). Update any code that built the authorize URL with `client_id` or `scope`.
- Consumers on an older client library do not announce `backChannelLogoutUri`, so they get no back-channel
  notifications until they are updated. The periodic session status check still applies.
- Old application records keep working as observed records; their old fields are ignored.
- Each application needs `CloudLogin:PublicUrl`. `WithReference` supplies it. An application started outside the
  AppHost (for example a product that reads its own `appsettings`) must set it.
- Each application needs an endpoint that receives `POST /auth/backchannel-logout` and a shared
  `IDistributedCache` if it runs more than one instance.
- Application cookies issued before the upgrade carry no session binding and are rejected once, so people sign
  in again. Set `RequireSessionBinding` to `false` only as a temporary bridge.
- Anything still depending on the plain `requestId` redirect must be moved to the transaction flow or run with
  `AllowLegacyRedirectHandoff`.
- Refresh tokens issued before this release that name no client are refused, so people sign in again. Callers of
  `Token/Session` receive an access token and no refresh token.
- Code that implements `ICloudLoginSessionRevocations` must follow the two-step logout-token methods
  (`IsLogoutTokenProcessedAsync`, `MarkLogoutTokenProcessedAsync`).
- Existing `AllowedRedirectOrigins` still apply to the legacy hand-off and to logout destinations that do not
  come from a transaction. They are not consulted for application authentication.

## Native applications (MAUI)

A native app signs in through its own backend, which is already the CloudLogin client. The app holds no credential and the authority
needs no registration of it. Nothing is added to the AppHost.

1. The app makes a PKCE verifier and a `state`, keeps them in secure storage, and opens the system browser (`WebAuthenticator`) at
   `GET {backend}/auth/native/login?challenge=<S256 of the verifier>&state=...&redirect_uri=blusky://auth/callback`.
2. The backend validates the `redirect_uri` (a scheme listed in `NativeCallbackSchemes`, nothing else, no query, fragment or
   credentials) and runs the ordinary website sign-in: a transaction, the correlation cookie in the system browser, the code redeemed with
   PKCE at the callback it already has (`/auth/callback`). No extra return URI is registered.
3. At the callback the backend builds the session it would have set as a cookie and, instead of setting it in the browser, keeps it as a
   60 second handoff in `IDistributedCache` together with the app's challenge, then redirects to
   `blusky://auth/callback?handoff=...&state=...`.
4. The app checks the `state` against the one it kept, then `POST {backend}/auth/native/exchange` with `{ handoff, verifier }`. The
   backend spends the handoff, checks the verifier against the challenge (a wrong verifier spends it too), and answers with the session
   cookie and the signed-in person. Only the app that started the sign-in holds the verifier, so an app that merely receives the custom-scheme
   URL (another app registered for the same scheme, a link) cannot redeem it. The cookie is persistent and carries the authority session id,
   so back-channel logout ends it like any other session.

Sign-out runs the same way: `GET {backend}/auth/native/logout?redirect_uri=...&state=...` from the system browser opens an authenticated
logout transaction, the authority ends the session in that browser, and the backend returns to the app with `operation=logout`. A request
that comes from another site is refused. Because the authority session ends, the back-channel notification ends the app's session too.

The backend sets `CloudLoginServerConfiguration.NativeCallbackSchemes` (for example `["blusky"]`); with it empty the endpoints are closed.
The app calls `AddMauiCloudLogin(new MauiCloudLoginOptions { ApplicationUrl = ..., CallbackScheme = "blusky" })` and registers an
`IMauiCloudLoginNativeExchange`, which keeps the cookie in the app's own HTTP stack (`MauiCloudLoginNativeClient.ExchangeAsync` does the
request). Several instances of the backend need the shared `IDistributedCache` and Data Protection key ring they already need for
back-channel logout.

The legacy `requestId` hand-off is no longer used by the MAUI library, so `AllowLegacyRedirectHandoff` and `AllowedMobileSchemes` are not
needed for it.

## Deploying

1. Deploy the authority first, with the `SecretKeys` container in place: it publishes the new discovery document
   and accepts the new endpoints, and the old consumer flow keeps working only if `AllowLegacyRedirectHandoff` is
   on.
2. Deploy each application with `CloudLogin:PublicUrl` and, for a website with a backend, its secret key. With
   CoconutSharp the generated secret key is stored in the environment Key Vault as `generated-{parameter}` and
   kept across redeployments.
3. Confirm in the admin console that each application appears under Applications after its first sign-in, and
   that the deployment key shows a recent last use under Secret keys.
4. Sign in and out on two applications and check that both end, and read `Logout.Notified` in the audit trail.

## Verification

The behavior above is covered by automated tests that run the authority and applications as separate servers
with independent cookie jars: login CSRF, replay, tampering and expiry, PKCE and binding failures, session
ownership, refresh re-checks, dynamic-hostname logout, two applications and several instances, forged, replayed
and duplicate back-channel tokens, failed delivery and the status backstop, public clients, secret keys and their
revocation, blocked applications, persistence of administrator decisions across restarts, and the AppHost wiring
in run and publish modes.

The registry-outage, forged-logout-reference, unbound-refresh and retry-safe back-channel scenarios each have a
regression test that fails against the previous behavior.

Not verified against live infrastructure: a distributed cache backed by a real service, real Cosmos containers,
and delivery across the network between separately hosted applications. These need a staging deployment.
