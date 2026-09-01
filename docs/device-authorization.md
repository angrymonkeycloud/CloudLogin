# CloudLogin device authorization (QR and TV sign-in)

CloudLogin implements QR sign-in with OAuth 2.0 Device Authorization Grant semantics (RFC 8628). A device with constrained input — a TV, a kiosk — asks the authority for a device authorization; the person approves it on their own phone, already signed in or signing in there with the profile's configured methods. QR is a transport mechanism, not an identity provider: nothing about scanning a code authenticates anyone.

## Flow

```
TV                                  Authority                          Phone
POST /api/v3/device/authorize  ──►  creates request (hashes only)
◄── device_code, user_code,
    verification_uri(,_complete),
    expires_in, interval

shows QR of verification_uri        person scans, opens /device        GET /api/v3/device/pending?user_code=...
and displays the user code                                             ◄── client description + code
                                                                       person compares codes, confirms
POST /api/v3/device/token      ──►  pending / slow_down                POST /api/v3/device/approve
      (repeat at interval)                                                  { user_code, confirmClient: true }
POST /api/v3/device/token      ──►  approved: consumed atomically
◄── { request_id }                       (exactly one winning poll)

completes sign-in with the single-use request id through the standard handoff
```

## Security properties

- **High-entropy codes, hashes at rest.** The `device_code` is 32 random bytes; only SHA-256 hashes of both codes are persisted, and the document id is the device-code hash so polling is a point read.
- **Phishing resistance.** The QR encodes only the verification URL. The short user code is displayed beside it and on the approval page, so the person confirms they are approving the request in front of them; the approval requires an authenticated user and an explicit `confirmClient` acknowledgment of the requesting device's description.
- **States** — `Pending`, `Approved`, `Denied`, `Consumed`, `Expired` — with every transition an ETag-conditional replace: approval and consumption each happen exactly once no matter how many callers race.
- **Polling discipline.** Responses are `no-store` and rate limited. Polls arriving faster than `interval` get `slow_down`; persistent violations (`MaxPollViolations`) deny the request outright.
- **Native expiry.** Requests live in the `LoginRequests` container with a positive `ttl` recomputed from `ExpiresOn` on every write; expired requests answer `expired_token` even before Cosmos removes them.
- **Sign-in profile binding.** The profile resolved when the flow started (for example `tv`) is stored on the request; URL tampering later cannot change it. Approval is then checked against it: the method the approving person signed in with on their phone must be one the profile allows, so a profile that requires a passkey cannot be satisfied by approving from a session that was itself started with a password. The method is read from the approver's own ticket, never from the request.
- The approved poll returns a **single-use login request id** consumed through the same atomic handoff every other sign-in uses.

## Endpoints

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `POST /api/v3/device/authorize?profile=tv&client=...` | anonymous, rate limited | Start; returns RFC 8628 fields |
| `POST /api/v3/device/token` | anonymous, rate limited | Poll with `device_code`; RFC errors `authorization_pending`, `slow_down`, `access_denied`, `expired_token` |
| `GET /api/v3/device/pending?user_code=...` | authenticated | What the person is about to approve |
| `POST /api/v3/device/approve` | authenticated | Approve; requires `confirmClient` |
| `POST /api/v3/device/deny` | authenticated | Deny |

## Configuration

```csharp
options.Core = new CloudLoginCoreConfiguration
{
    DeviceAuthorization = new DeviceAuthorizationConfiguration
    {
        CodeLifetime = TimeSpan.FromMinutes(10),
        PollIntervalSeconds = 5,
        MaxPollViolations = 30,
        UserCodeLength = 8,
        VerificationPath = "/device"
    }
};
```

User codes use an unambiguous alphabet (no vowels, no `0/O/1/I`) and display as `XXXX-XXXX`; entry is case- and separator-insensitive.

## Related pages

- [docs/signin-profiles.md](signin-profiles.md)
- [docs/architecture-core.md](architecture-core.md)
