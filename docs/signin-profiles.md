# CloudLogin sign-in profiles

A sign-in profile is a named, configurable sign-in experience selected by the login URL — `https://login.example.com/?profile=tv`. Profiles restrict which entry methods a login page displays and which authorization methods may complete the flow; they can only narrow what the deployment already configured, never enable anything new. URL parameters never directly enable providers or capabilities.

## Configuration

```csharp
builder.AddCloudLoginWeb(options =>
{
    options.SignInProfiles.Profiles =
    [
        new CloudLoginSignInProfile { Name = "default" }, // all client-allowed methods
        new CloudLoginSignInProfile
        {
            Name = "tv",
            VisibleMethods = [ "Qr" ],                    // the TV page shows QR only
            AllowedMethods = [ "Password", "Google" ]     // the mobile approval page uses these
        },
        new CloudLoginSignInProfile
        {
            Name = "passwordless",
            VisibleMethods = [ "Code", "Google" ],
            AllowedMethods = [ "Code", "Google" ]
        }
    ];

    options.SignInProfiles.DefaultProfile = "default";

    // A client must explicitly allow every non-default profile it may request.
    options.SignInProfiles.ClientProfiles["https://tv.example"] = [ "tv" ];
    options.SignInProfiles.ClientProfiles["https://app.example"] = [ "passwordless" ];
});
```

Startup validation rejects unnamed or duplicate profiles, a `DefaultProfile` that is not configured, and client allowances that reference unknown profiles.

## Resolution rules

- No profile requested: the configured default.
- Unknown profile name: fails safely to the default (never an error page an attacker can probe).
- Known profile, but the requesting client has no allowance for it: the default.
- The default profile itself needs no allowance.

`GET /api/v3/signin-profile?profile=tv&client=https://tv.example` returns the resolved profile, the visible methods filtered against the deployment's configured providers, and a `BoundState` value.

## Where the restriction is enforced

Every entry path checks the profile, not just the provider redirect — a profile that lists only `Qr` would be worth nothing if the password form still signed people in.

| Method | Where it is checked |
| --- | --- |
| Provider (Google, Microsoft, …) | At challenge time in `Login`, and again on callback against the profile sealed into the authentication ticket |
| Password, test mode | `SignInUserAsync`, the single choke point for every sign-in that does not go through a provider challenge |
| Verification code | `CustomLogin`, where the code flow completes |
| QR / device approval | On approve, against the profile stored on the device request — see [device-authorization.md](device-authorization.md) |

The direct methods have no ticket to carry a sealed profile, so they resolve the request's profile the same way the challenge does. Resolution already falls back to the default for an unknown or unauthorized name, so a forged `?profile=` parameter can only ever narrow what a caller is allowed to do, never widen it.

## Tamper resistance

The resolved profile is sealed into Data Protection-protected state (`BoundState`) at resolution time and bound into device authorization requests when a flow starts. Completion paths validate the sealed state — changing `?profile=` in a URL mid-flow cannot change the profile that governs authorization, and a tampered payload unbinds to nothing rather than falling back to a guess, forcing a flow restart.

## The TV pattern

The `tv` profile above shows only the QR entry on the television while the person's phone — where approval actually happens — authenticates with the profile's `AllowedMethods`. QR is a transport, not an identity provider; see [docs/device-authorization.md](device-authorization.md).
