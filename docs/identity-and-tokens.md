# Identity and tokens

## The rule

**Identity is never a parameter.** It is derived from a verified credential on the
request — a session cookie this application issued, or an access token signed by the
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
| Service client secret | until rotated | server config / secret store | *this service* is who it says it is |

Access tokens are short-lived because they cannot be revoked once minted — the
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
  "AllowedAudiences": [ "portal", "cdm-api" ],
  "ServiceClients": {
    "portal": {
      "ClientId": "portal",
      "SecretHash": "<base64 SHA-256 of the secret>",
      "AllowedAudiences": [ "cdm-api" ]
    }
  }
}
```

Generate a secret hash with:

```bash
pwsh -c '[Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("<secret>")))'
```

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
you are either filtering data (fine — say `UserIds`, and the server still applies the
caller's own permissions on top) or reintroducing impersonation (not fine).

## Registering the client as a service client

The sign-in callback exchanges the single-use login request id for tokens by
presenting the application's client credentials. Without `ClientId` and
`ClientSecret` configured, sign-in still works, but the application receives no
tokens and cannot prove the user's identity to any other service — downstream calls
will be anonymous and get 401s.

## Key rotation

Signing keys are ES256, generated on first use and rotated every 30 days. A retired
key stays published in JWKS for a further two hours so tokens already in flight keep
verifying. Private keys are wrapped with Data Protection before storage, so a
database disclosure alone does not allow forging tokens.

Rotation is automatic. `SigningKeyPublishGrace` must always exceed
`AccessTokenLifetime`; the options validator enforces this at startup.

Production deployments can move signing entirely into Azure Key Vault or Managed HSM by
setting `CloudLoginTokens:SigningKeys:KeyVaultKeyId`: the key is created non-exportable,
every signature is computed inside the vault, and rotation becomes the vault's own
key-version rotation. That is the recommendation, not a requirement — the Cosmos fallback
is Data Protection-wrapped and TTL-retired, so a deployment that configures nothing still
runs. Set `SigningKeys:RequireExplicitStoreChoice` to make the choice mandatory where policy
demands it. See [architecture-core.md](architecture-core.md).

## What is deliberately not supported

- Any endpoint that accepts a user id and returns a token for that user.
- Validating a token without checking its audience.
- Accepting a token signed with anything other than ES256, which defeats algorithm
  confusion attacks.
- Storing a refresh token in the clear, on either side.
