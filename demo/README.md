# CloudLogin demo suite

The demo suite contains three runnable applications with no external database, OAuth
registration, or SMTP requirement. Each uses in-memory state that resets when the process
restarts and seeds a Demo Admin account for the administration UI.

| App | Default URL | Purpose |
| --- | --- | --- |
| [`CloudLogin.Demo`](CloudLogin.Demo) | `https://localhost:7100` | Standalone CloudLogin authority with login, account, administration, and recovery flows. |
| [`CloudLogin.Demo.Consumer`](CloudLogin.Demo.Consumer) | `https://localhost:7200` | Consumer-site integration through the cookie redirect and confidential token exchange, including coordinated logout. |
| [`CloudLogin.Demo.Embedded`](CloudLogin.Demo.Embedded) | `https://localhost:7300` | Comprehensive component and account-registry showcase embedded in a custom host. |

## Run the demos

Start any app in its own terminal:

```bash
dotnet run --project demo/CloudLogin.Demo
dotnet run --project demo/CloudLogin.Demo.Consumer
dotnet run --project demo/CloudLogin.Demo.Embedded
```

The Consumer app expects the Authority app at `https://localhost:7100`; start the Authority
first for that flow. The Embedded demo is self-contained.

## Sign in and verify accounts

- **Test Mode** is the quickest route. Choose Demo Admin to unlock the global-administration
  features or create a generated regular user.
- **Password** supports registration, sign-in, and recovery with a password of at least 12
  characters.
- **Email verification code** uses the real CloudLogin code-provider pipeline. In the
  Embedded demo, request a code on `/login` and read it from `/inbox`. The Authority demo
  exposes its standalone inbox at `https://localhost:7100/demo/inbox.html`.
## Standalone authority showcase

The standalone `CloudLogin.Demo` now wraps the packaged authentication and account pages in
a developer navigation shell. Open `https://localhost:7100/demo` to explore account-registry
features without signing in. All records are seeded once through CloudLogin's public services
when the application starts.

| Route | Seeded showcase |
| --- | --- |
| `/` | Packaged authentication UI with Password, Code, and Test Mode providers. |
| `/Account` | Packaged account and global-administration UI. |
| `/demo` | Feature overview and seed-data summary. |
| `/demo/workspaces` | Two workspaces, owners, members, application-defined roles and permissions, and expiring invitations. |
| `/demo/subscriptions` | Active and expired user subscriptions plus active Cedar Labs and Northstar Clinic subscriptions. |
| `/demo/billing` | User and workspace billing profiles with Stripe, MyFatoorah, and SkipCash references. |
| `/demo/inbox.html` | Verification codes captured from the real demo callback. |

Each account-registry page includes expandable integration code. The records deliberately
show both user and workspace ownership, different subscription states, renewal behavior,
application metadata, provider references, and multiple saved payment methods.

- **External OAuth and WhatsApp** require real provider credentials and are intentionally not
  faked. Their adapters can be registered in a configured application.

## Embedded developer showcase

The Embedded demo identifies the real artifact on every example, renders a working Preview,
and provides a Code tab with registration or Razor integration code. Demo-only infrastructure
is explicitly labelled so it cannot be confused with a package component.

| Route | Public API demonstrated |
| --- | --- |
| `/login` | Shipped `CloudLoginPage` with Test Mode, Password, and email-code providers. |
| `/providers` | Provider registration, production credential expectations, and the authentication pipeline. |
| `/account` | Shipped `AccountPageComponent` with profile, contacts, administration, and account lifecycle features. |
| `/workspaces` | `ICloudLoginWorkspaceRegistry`: workspaces, members, roles, permissions, and expiring invitations. |
| `/subscriptions` | `ICloudLoginSubscriptionRegistry`: user and workspace subscriptions, status, expiry, auto-renew, provider references, and application metadata. |
| `/billing` | `ICloudLoginAccountStore`: account-level provider customer and saved payment-method references. |
| `/inbox` | Demo-only verification-code capture wired to the real email-code callback. |

The workspace, subscription, and billing labs perform real calls against CloudLogin's
public services. The demo supplies a scoped in-memory adapter; production applications can
replace it with a private database adapter without adding a database dependency to public
CloudLogin packages. CloudLogin stores billing references but never authorizes, captures, or
refunds payments.

See [`docs/account-registry.md`](../docs/account-registry.md) for the complete account-registry
boundary and API.
