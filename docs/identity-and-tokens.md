# Identity and tokens

## The rule

**Identity is never a parameter.** It is derived from a verified credential on the
request: a session cookie this application issued, or an access token signed by the
CloudLogin authority and verified against its published public key.

A user id in a request body or query string is data the *caller* chose. Using it for
authorization means the caller picks who they are. That is why `ExternalRequestBase`
has no `UserId`, why no client method takes a `userId`, and why there is no endpoint
anywhere that turns a bare user id into a token.

## What each piece is for

| Credential | Lifetime | Who holds it | What it proves |
|---|---|---|---|
| Session cookie | hours | browser / native HTTP stack | this browser session signed in |
| Access token | 10 min | server-side, inside the cookie | *this user* is making *this call* to *this audience* |
| Refresh token | 14 days, rotating | server-side, inside the cookie | this session may mint a new access token |
| Secret key | until revoked or expired | server config / secret store | *this backend* may act as a CloudLogin client |

Access tokens are short-lived because they cannot be revoked once minted: the
lifetime **is** the revocation window. Refresh tokens are long-lived but single-use
and revocable, so a leaked one is usable at most once before reuse detection burns
the whole chain.

## Why cookies for browsers, tokens between services

Cookies are marked `HttpOnly`, so JavaScript cannot read them; a token in
`localStorage` can be exfiltrated by any XSS. So the browser keeps a cookie, and the
access and refresh tokens ride *inside* that cookie's encrypted payload, server-side.
When the server calls a downstream API it takes the token out and attaches it.

This is the backend-for-frontend pattern, and it is why the MAUI app needs no client
secret and holds no bearer token: it completes a server-side exchange and carries
only a session cookie in its native cookie container.

## Setting up a new application

Two calls. Everything else is automatic.

**The authority** (one per environment):

```csharp
builder.Services.AddCloudLoginTokenIssuer(builder.Configuration.GetSection("CloudLoginTokens"));
```

```jsonc
"CloudLoginTokens": {
  "Issuer": "https://login.example.com",
  "SecretKeys": [ "<at least 32 characters, from a secret store>" ]
}
```

`SecretKeys` is optional: keys can also be created in the admin console. Any active key
works for any application name, so nothing per application is configured here. An Aspire
AppHost writes one key for you.

**A relying party** (every app that authenticates users or exposes an API):

```csharp
builder.Services.AddCloudLoginTokenAuthentication(options =>
{
    options.Authority = builder.Configuration["CloudLogin:Authority"]!;
    options.Audience = "portal";
    options.ClientId = "portal";
    options.ClientSecret = builder.Configuration["CloudLogin:ClientSecret"];

    // API-only hosts should turn this on. Hosts that also serve anonymous pages or
    // the sign-in callback must leave it off and use [Authorize] on controllers,
    // because the fallback policy would otherwise block the sign-in flow itself.
    options.RequireAuthenticatedByDefault = false;
});
```

That one call registers everything: bearer validation against the authority's JWKS,
a policy scheme that picks cookie or bearer per request, `ICloudLoginUserContext` for
reading the caller, and `CloudLoginTokenHandler` for attaching the caller's token to
outbound requests.

## Writing code against it

**Reading who is calling:**

```csharp
public sealed class ThingController(ICloudLoginUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        Guid userId = currentUser.RequireUserId();   // throws if anonymous
        ...
    }
}
```

**Calling a downstream API on the user's behalf:**

```csharp
builder.Services
    .AddHttpClient<MyPortalClient>(client => client.BaseAddress = portalUri)
    .AddHttpMessageHandler<CloudLoginTokenHandler>();
```

```csharp
await portal.Contact.SaveAsync(contact);   // identity travels automatically
```

There is no step where a user id is passed. If you find yourself wanting to pass one,
you are either filtering data (fine: say `UserIds`, and the server still applies the
caller's own permissions on top) or reintroducing impersonation (not fine).

## Signing in from a website with a backend

The sign-in callback redeems a single-use authorization code, bound to the client name, the callback and a PKCE
challenge, for tokens by presenting the application's name and secret key with HTTP Basic. Without `ClientId`
and `ClientSecret` configured, the application cannot start a sign-in at all, because the authority only opens a
sign-in for a caller holding a valid key. The application also needs `CloudLogin:PublicUrl`, the address its
callback and back-channel logout URL are built from. A website without a backend or a native app needs no key:
it is identified by its own origin, and its tokens are valid only there. See
[sign-in-and-logout.md](sign-in-and-logout.md).

## Applications in the admin

Applications are not registered anywhere. An application appears in the admin console the first time it
authenticates or signs someone in, with where it was seen, the key it last used and its active sessions. An
administrator can block it (it is refused from then on and its sessions end), unblock it, end its sessions, or
forget the record, and can create and revoke the secret keys backends authenticate with. See
[admin-portal.md](admin-portal.md).

## Access tokens and refresh

Access tokens are stateless ES256 JWTs that live for at most `AccessTokenLifetime` (default 10 minutes) and
cannot be revoked. Refresh tokens are bound to the client that received them, rotate on every use, and are
checked each time: the caller must present a valid key and the same name (or, for a native app, its scheme), and
the application must not be blocked. Ending a session revokes its refresh tokens at once and tells each
application so through back-channel logout.

## Key rotation

Signing keys are ES256, generated on first use and rotated every 30 days. A retired
key stays published in JWKS for a further two hours so tokens already in flight keep
verifying. Private keys are wrapped with Data Protection before storage, so a
database disclosure alone does not allow forging tokens.

Rotation is automatic. `SigningKeyPublishGrace` must always exceed
`AccessTokenLifetime`; the options validator enforces this at startup.
An administrator can also rotate on demand from the console: the new key signs at once and the outgoing one
stays published for the same grace period.

Production deployments can move signing entirely into Azure Key Vault or Managed HSM by
setting `CloudLoginTokens:SigningKeys:KeyVaultKeyId`: the key is created non-exportable,
every signature is computed inside the vault, and rotation becomes the vault's own
key-version rotation. That is the recommendation, not a requirement: the Cosmos fallback
is Data Protection-wrapped and TTL-retired, so a deployment that configures nothing still
runs. Set `SigningKeys:RequireExplicitStoreChoice` to make the choice mandatory where policy
demands it. See [architecture-core.md](architecture-core.md).

## What is deliberately not supported

- Any endpoint that accepts a user id and returns a token for that user.
- Validating a token without checking its audience.
- Accepting a token signed with anything other than ES256, which defeats algorithm
  confusion attacks.
- Storing a refresh token in the clear, on either side.
