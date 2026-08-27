# CloudLogin
[![Website](https://img.shields.io/badge/Website-angrymonkeycloud.com-0B5FFF?style=flat-square&logo=googlechrome&logoColor=white)](https://angrymonkeycloud.com/cloudlogin)
[![GitHub repository](https://img.shields.io/badge/GitHub-CloudLogin-181717?style=flat-square&logo=github)](https://github.com/angrymonkeycloud/CloudLogin)
[![Tests](https://github.com/angrymonkeycloud/CloudLogin/actions/workflows/tests.yml/badge.svg)](https://github.com/angrymonkeycloud/CloudLogin/actions/workflows/tests.yml)
[![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Client?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Client)
[![NuGet downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Client?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Client)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-2F855A?style=flat-square)](LICENSE)

Authentication, account, profile, and coordinated-session packages for .NET 10, Blazor, and .NET MAUI.

CloudLogin is secure by default: HTTPS-only cookies, exact redirect allowlists, encrypted authentication tickets and return state, rate-limited authentication endpoints, protected profile mutations, modern password hashing, and coordinated authority logout are enabled by the standard registration methods.

## Table of contents

- [Try it locally](#try-it-locally)
- [Projects and packages](#projects-and-packages)
- [Architecture](#architecture)
- [Standalone login website](#standalone-login-website)
- [Consumer website](#consumer-website)
- [Embedded website](#embedded-website)
- [.NET MAUI](#net-maui)
- [Aspire integration](#aspire-integration)
- [Feature overview](#feature-overview)
- [Authentication providers](#authentication-providers)
- [Configuration reference](#configuration-reference)
- [Database schema](#database-schema)
- [Endpoints developers commonly use](#endpoints-developers-commonly-use)
- [Developer implementation checklist](#developer-implementation-checklist)
- [Secure defaults](#secure-defaults)
- [Production key and secret management](#production-key-and-secret-management)
- [Production startup troubleshooting](#production-startup-troubleshooting)
- [Migration notes](#migration-notes)
- [Security scope](#security-scope)
- [Additional guidance](#additional-guidance)

## Try it locally

The [`demo/`](demo/README.md) folder has three runnable apps you can `dotnet run` with zero
setup - no Cosmos DB, no OAuth secrets, no SMTP server - covering the standalone authority,
consumer-website integration, and embedded-host integration. See [`demo/README.md`](demo/README.md)
for ports and how to sign in.

## Projects and packages

| Package | Version | Downloads | Use |
| --- | --- | --- | --- |
| `AngryMonkey.CloudLogin.Web` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Web?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Web) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Web?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Web) | Standalone CloudLogin website |
| `AngryMonkey.CloudLogin.Server` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Server?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Server) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Server?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Server) | Consumer website or embedded server integration |
| `AngryMonkey.CloudLogin.API` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.API?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.API) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.API?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.API) | HTTP endpoints exposing the server over `CloudLogin.Client` |
| `AngryMonkey.CloudLogin.Maui` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Maui?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Maui) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Maui?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Maui) | Native .NET MAUI authentication |
| `AngryMonkey.CloudLogin` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin) | Blazor routing/layout shared by `CloudLogin.Maui` and other Blazor-hybrid hosts |
| `AngryMonkey.CloudLogin.Components` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Components?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Components) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Components?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Components) | The login/account/profile Blazor UI itself |
| `AngryMonkey.CloudLogin.WebAssembly` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.WebAssembly?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.WebAssembly) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.WebAssembly?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.WebAssembly) | WebAssembly runtime used by the standalone UI package |
| `AngryMonkey.CloudLogin.Client` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Client?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Client) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Client?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Client) | Typed CloudLogin HTTP client |
| `AngryMonkey.CloudLogin.Contracts` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Contracts?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Contracts) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Contracts?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Contracts) | Shared models and URL contracts |
| `AngryMonkey.CloudLogin.Aspire.Hosting` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Aspire.Hosting?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Aspire.Hosting) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Aspire.Hosting?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Aspire.Hosting) | AppHost-side: adds CloudLogin to a .NET Aspire distributed application |
| `AngryMonkey.CloudLogin.Aspire` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogin.Aspire?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Aspire) | [![Downloads](https://img.shields.io/nuget/dt/AngryMonkey.CloudLogin.Aspire?style=flat-square&logo=nuget)](https://www.nuget.org/packages/AngryMonkey.CloudLogin.Aspire) | Server-side: binds the config an Aspire AppHost wires (see [Aspire integration](#aspire-integration)) |

## Architecture

CloudLogin is organized into a few clear layers:

- **Contracts layer** (`AngryMonkey.CloudLogin.Contracts`): shared DTOs, request/response models, provider definitions, and routing helpers.
- **Authority runtime layer** (`AngryMonkey.CloudLogin.Server` + `AngryMonkey.CloudLogin.API`): authentication orchestration, provider wiring, account/user/request endpoints, and security enforcement.
- **Authority host/UI layer** (`AngryMonkey.CloudLogin.Web`, `AngryMonkey.CloudLogin.Components`, `AngryMonkey.CloudLogin.WebAssembly`): standalone Blazor UI and account/login component experience.
- **Consumer integration layer** (`AngryMonkey.CloudLogin.Server` package usage in external apps): redirects to authority, callback handling, local cookie issuance, coordinated logout.
- **Mobile layer** (`AngryMonkey.CloudLogin.Maui`): MAUI-native login initiation, callback handling, and secure local session storage.
- **Client SDK layer** (`AngryMonkey.CloudLogin.Client`): typed API access for programmatic interactions.

Relationship map:

- `CloudLogin.Web` hosts the authority UI and composes `CloudLogin.Components` + `CloudLogin.WebAssembly`.
- `CloudLogin.Server` depends on contract models and configures provider authentication plus security policies.
- `CloudLogin.API` exposes HTTP endpoints that call into `ICloudLogin`/server services.
- `CloudLogin.Client` consumes the API surface using the same models from `CloudLogin.Contracts`.
- Consumer websites and MAUI apps authenticate against the CloudLogin authority URL.

Integration patterns:

1. **Standalone authority + consumer website(s)** (most common)
   - Deploy one CloudLogin authority site.
   - Consumer apps use `AddCloudLoginServer("https://login.example.com")`.
2. **Embedded authority in existing ASP.NET Core host**
   - Add CloudLogin services/components directly to an existing site.
3. **Mobile + authority**
   - MAUI app uses `AddMauiCloudLogin(...)` and authority callback scheme allowlist.

## Standalone login website

The complete setup is configuration plus one run call:

```csharp
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.AddCloudLoginWeb(options =>
{
    options.Cosmos = new(builder.Configuration.GetSection("Cosmos"));
    options.AzureStorage = new(builder.Configuration.GetSection("Storage"));
    options.Subscription = new SubscriptionConfiguration();
    options.Workspace = new WorkspaceConfiguration();
    options.Payment = new PaymentConfiguration();
    options.Providers =
    [
        new LoginProviders.GoogleProviderConfiguration(
            builder.Configuration.GetSection("Google"))
    ];

    // Optional UI customization.
    options.PrimaryColor = "#0078D4"; // Defaults to blue when omitted.
    options.WebConfig = web => web.PageDefaults.SetTitle("Company Login");
});

await CloudLoginWeb.InitApp(builder);
```

Subscription, workspace, and payment account features are opt-in. Adding the
corresponding configuration enables its account navigation item, page, and API;
omitting it keeps that complete feature surface disabled.

### Workspace allowances

`WorkspaceConfiguration` caps how many workspaces one user may accumulate. Both
caps are optional: leave them unset and CloudLogin applies its defaults.

```csharp
options.Workspace = new WorkspaceConfiguration
{
    // How many workspaces one user may create and own. Default: 3.
    MaxOwnedPerUser = 5,

    // How many workspaces one user may belong to in total, owned ones
    // included. Default: 10.
    MaxPerUser = 20
};
```

- Set either cap to `WorkspaceLimits.Unlimited` to remove it.
- Set `MaxOwnedPerUser = 0` to stop users creating workspaces entirely while still
  letting them be invited into ones an administrator provisions.
- A `MaxPerUser` below `MaxOwnedPerUser` wins, since every owned workspace is also a
  membership.

Creating past the cap throws `WorkspaceLimitReachedException`, which the account API
returns as `409 Conflict` with the reason in the ProblemDetails `detail`. The account page
reads the user's current standing from `GetMyWorkspaceQuota()` and shows it as two
meters, so the limit is visible before it's hit rather than only when a create is refused.

"Workspace" is deliberately generic — it's the internal name for the concept everywhere
in code, API routes, JSON, and webhook events, so it stays stable across every product
that embeds CloudLogin. What end users actually *see* is configurable per deployment via
`SingularLabel`/`PluralLabel`:

```csharp
options.Workspace = new WorkspaceConfiguration
{
    SingularLabel = "Organization",
    PluralLabel = "Organizations"
    // Or "Business"/"Businesses", "Team"/"Teams" — whatever the concept is
    // called in your product. Defaults to "Workspace"/"Workspaces".
};
```

The account UI reads these labels everywhere it names the concept — field labels,
buttons, dialog titles, and messages like `WorkspaceLimitReachedException`'s. Routes,
JSON property names, and webhook event names (`Workspace.Created`, `Workspace.Updated`, …)
are unaffected; only the label a user reads changes.

### Workspaces as accounts

A user switches workspace from the account rail: their personal account, or any
workspace they belong to. Inside a workspace the same account page shows that
workspace's information, billing information, members, subscriptions, and payment
methods, each scoped to the workspace instead of the user. `GetWorkspaceDetail(id)`
returns all of it in one call and answers `null` for non-members, so an identifier alone
never reveals someone else's workspace.

Reading a workspace's detail requires membership; editing its profile, billing, and
payment methods requires its owner or a member holding the `Admin` role. Deleting it
requires the owner.

### Subscription deletion policy

`CloudSubscription.DeletionPolicy` decides when a registry entry may be removed, and by
extension when its workspace can be deleted:

| Policy | Meaning |
| --- | --- |
| `WhenExpired` | **Default.** Removable once the subscription has stopped running — past its expiry, or `Cancelled`/`Expired`. Blocks workspace deletion while it is still running. |
| `Always` | Removable at any time, running or not. |
| `Never` | Never removable through the account surface. Blocks workspace deletion until the owning application clears it. |

Deleting a workspace removes its memberships, invitations, billing profile, and
remaining subscription records in one operation, and publishes `Workspace.Deleted`.
It is refused with `WorkspaceDeletionBlockedException` while any subscription still
blocks it; `GetDeletionReportAsync` returns the same blockers ahead of time, along with the
member, payment-method, and subscription counts the account page shows in its confirmation.

Removal deletes the registry entry only — it is not a cancellation, and the owning
application stays responsible for ending whatever the subscription paid for. Set
`SubscriptionConfiguration.AllowSelfServiceDeletion = false` to refuse every self-service
removal regardless of policy.

Custom `ICloudLoginAccountStore` implementations written before deletion existed keep
compiling: the removal members carry default implementations that report the store doesn't
support deletion. Implement them to let owners delete their workspaces.

The redirect and mobile allowlists are optional. With no additional configuration,
CloudLogin permits every relative, same-origin, and external redirect destination — there
is no allowlist to restrict against. Once you register at least one website or mobile
scheme, only the registered ones are trusted for that channel. Register callbacks for the
specific websites and apps you expect:

```csharp
builder.AddCloudLoginWeb(options =>
{
    options
        .AllowWebsite("https://app.example.com")
        .AllowWebsite("https://portal.example.com")
        .AllowMobileApp("myapp");

    // Other configuration...
});
```

HTTP website origins are accepted only for loopback development addresses. `Cosmos`,
Azure Storage, external providers, UI customization, website callbacks, and mobile
callbacks are feature-based configuration rather than startup requirements.

## Consumer website

Register CloudLogin using the authority URL:

```csharp
builder.Services.AddCloudLoginServer("https://login.example.com");
```

Or keep the URL in configuration:

```json
{
  "LoginUrl": "https://login.example.com"
}
```

```csharp
builder.Services.AddCloudLoginServer(new CloudLoginServerConfiguration
{
    LoginUrl = builder.Configuration["LoginUrl"]
});
```

Use the standard ASP.NET Core pipeline:

```csharp
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

The consumer callback state is encrypted and integrity-protected with ASP.NET Core Data Protection. Logout clears both the consumer cookie and the CloudLogin authority session before returning to the website.

## Embedded website

```csharp
builder.Services.AddCloudLoginEmbedded(loginOptions, builder.Configuration);

var app = builder.Build();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCloudLoginSecurity();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

`UseCloudLoginSecurity` enables authentication rate limiting, secure response headers, sensitive-response no-store headers, and same-origin checks for state-changing browser requests.

## .NET MAUI

```csharp
builder.AddMauiCloudLogin(
    "https://login.example.com",
    "myapp");
```

Add the same scheme with `AllowMobileApp("myapp")` on the authority. CloudLogin handles the platform callback, stores the local session in MAUI `SecureStorage`, and uses the system authentication browser. Logout clears both local secure storage and the authority browser session. An interrupted authority logout is retried before the next login.

## Aspire integration

`AngryMonkey.CloudLogin.Aspire.Hosting` adds CloudLogin to a .NET Aspire AppHost as an ordinary
project resource, with every other resource in the model able to sign in against it through one
call:

```csharp
using AngryMonkey.CloudLogin.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// The application's own CloudLogin server project:
var login = builder.AddCloudLogin<Projects.My_Login>("login");

// ...or the packaged server, with no project of your own:
var login = builder.AddCloudLogin("login");

// ...or an already-deployed authority, reached by URL:
var login = builder.AddCloudLogin("login", "https://login.example.com");

// Connect any project: sets LoginUrl, waits for the authority in run mode, and (for the two
// forms above) configures the redirect allow-list to match.
var web = builder.AddProject<Projects.Web>("web").WithCloudLogin(login);
```

A server that also reads CloudLogin-owned records directly - CDM's external data provider, for
example - additionally calls `WithServiceAccess(login)`: that channel bypasses user identity, so
it is its own call rather than a flag on `WithReference`, granted only to the servers that need it.

On the server side, `AngryMonkey.CloudLogin.Aspire` is what a CloudLogin server hosted under Aspire
uses to configure itself from what the AppHost wired, instead of repeating Cosmos/Storage
connection details in its own `appsettings.json`:

```csharp
using AngryMonkey.CloudLogin.Aspire;

CloudLoginWebConfiguration configuration = new();
configuration.BindAspireResources(builder);
```

The same configuration keys work with no AppHost anywhere in the picture - an `appsettings.json`,
a user secret, an environment variable set by hand - so a CloudLogin server stays runnable outside
Aspire too. **[CoconutSharp](https://github.com/CoconutSharp/CoconutSharpAspire)** builds on this
package for environment-aware publishing (naming, identity, App Service targets per Dev/Staging/
Production) without changing how CloudLogin itself is configured.

## Feature overview

CloudLogin includes:

- Standalone authority website (`AngryMonkey.CloudLogin.Web`) with built-in login and account UI.
- Embedded authority mode for integrating CloudLogin directly inside an existing ASP.NET Core host.
- Consumer-site integration (`AngryMonkey.CloudLogin.Server`) with secure login, callback, profile redirect, and coordinated logout endpoints.
- .NET MAUI support (`AngryMonkey.CloudLogin.Maui`) with mobile callback scheme support.
- .NET Aspire support (`AngryMonkey.CloudLogin.Aspire.Hosting` + `AngryMonkey.CloudLogin.Aspire`) for AppHost composition and configuration binding - see [Aspire integration](#aspire-integration).
- Blazor/WebAssembly UI components (`AngryMonkey.CloudLogin.Components` + `AngryMonkey.CloudLogin.WebAssembly`) for login and account flows.
- Typed client/contracts packages for programmatic integration and shared models.
- Account profile management (name, locale, country, profile image upload).
- Multi-input identity support (email and phone number formats).
- Global admin user-management experience in account UI.
- Test mode provider for controlled test-account sign-in.

## Authentication providers

CloudLogin provider configurations are defined in `AngryMonkey.CloudLogin.Sever.Providers.LoginProviders` (and `LoginTestProviders`):

| Provider | Configuration type | External IdP | Handles | Typical use |
| --- | --- | --- | --- | --- |
| Password | `PasswordProviderConfiguration` | No | Email/password | Primary username+password sign-in |
| Code (OTP) | `CodeProviderConfiguration` | No | Email verification code | Passwordless/verification-code flow |
| Microsoft | `MicrosoftProviderConfiguration` | Yes | OAuth/OIDC + email claims | Microsoft Entra ID / Microsoft account sign-in |
| Google | `GoogleProviderConfiguration` | Yes | OAuth + profile claims | Google account sign-in |
| Facebook | `FacebookProviderConfiguration` | Yes | OAuth + profile claims | Facebook account sign-in |
| Twitter | `TwitterProviderConfiguration` | Yes | OAuth | X/Twitter account sign-in |
| WhatsApp | `WhatsAppProviderConfiguration` | No (custom transport) | Phone + verification code send path | Phone-based code delivery |
| Test Mode | `LoginTestProviders.TestModeConfiguration` | No | Internal test identities | Integration/UAT test sign-in |

Example mixed provider registration:

```csharp
builder.AddCloudLoginWeb(options =>
{
    options.Cosmos = new(builder.Configuration.GetSection("Cosmos"));
    options.AzureStorage = new(builder.Configuration.GetSection("Storage"));

    options.Providers =
    [
        new LoginProviders.PasswordProviderConfiguration(builder.Configuration.GetSection("Password")),
        new LoginProviders.CodeProviderConfiguration(builder.Configuration.GetSection("Code")),
        new LoginProviders.GoogleProviderConfiguration(builder.Configuration.GetSection("Google")),
        new LoginProviders.MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft")),
        new LoginProviders.FacebookProviderConfiguration(builder.Configuration.GetSection("Facebook")),
        new LoginProviders.TwitterProviderConfiguration(builder.Configuration.GetSection("Twitter")),
        new LoginProviders.WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp")),
        new LoginTestProviders.TestModeConfiguration(builder.Configuration.GetSection("TestMode"))
    ];
});
```

## Configuration reference

Minimal configuration template (fill only sections for enabled features/providers):

```json
{
  "Cosmos": {
    "ConnectionString": "<cosmos-connection-string>",
    "DatabaseId": "CloudLogin",
    "ContainerId": "Users"
  },
  "Storage": {
    "ConnectionString": "<storage-connection-string>",
    "PublicBaseUrl": "https://<account>.blob.core.windows.net/<container>"
  },
  "Google": {
    "ClientId": "<google-client-id>",
    "ClientSecret": "<google-client-secret>",
    "Label": "Google"
  },
  "Microsoft": {
    "ClientId": "<microsoft-client-id>",
    "ClientSecret": "<microsoft-client-secret>",
    "TenantId": "common",
    "Label": "Microsoft"
  },
  "Facebook": {
    "ClientId": "<facebook-client-id>",
    "ClientSecret": "<facebook-client-secret>",
    "Label": "Facebook"
  },
  "Twitter": {
    "ClientId": "<twitter-api-key>",
    "ClientSecret": "<twitter-api-secret>",
    "Label": "Twitter"
  },
  "WhatsApp": {
    "RequestUri": "<provider-request-uri>",
    "Authorization": "<auth-header-value>",
    "Template": "<message-template>",
    "Language": "en",
    "Label": "WhatsApp"
  },
  "TestMode": {
    "IsEnabled": false,
    "Label": "Test Mode"
  }
}
```

## Database schema

CloudLogin's own Cosmos DB and Azure Blob Storage documents — every container, field, and a realistic JSON sample — are documented in [`docs/database-schema.md`](docs/database-schema.md).

## Endpoints developers commonly use

Consumer-site endpoints from `AuthController`:

- `GET /auth/login?returnUrl=/path` - start login at CloudLogin authority.
- `GET /auth/callback` - login callback that creates the local auth cookie.
- `GET /auth/profile?returnUrl=/path` - redirect authenticated user to authority account page.
- `GET /auth/profileCallback` - returns to local application from account flow.
- `GET|POST /auth/logout?returnUrl=/` - coordinated logout (consumer + authority).

Authority/API endpoints (selected):

- `GET /CloudLogin/Login/{identity}` - begin provider flow.
- `GET /CloudLogin/Result` - OAuth/OIDC callback target.
- `GET /CloudLogin/Login/Complete` - build completion redirect.
- `GET /CloudLogin/Logout` - authority logout endpoint.
- `GET /api/Providers` - list available providers for UI.
- `GET /CloudLogin/User/GetUserByInput` - public account discovery (rate-limited, transport-safe).
- `POST /CloudLogin/User/Update` - authenticated profile update with server-side field protections.
- `POST /CloudLogin/User/UploadProfilePicture` - authenticated profile image upload.
- `GET /CloudLogin/Request/GetUserByRequestId` - resolve request-id to transport-safe user model.

## Developer implementation checklist

1. Select deployment mode: standalone authority, consumer integration, or embedded.
2. Configure HTTPS and redirect/mobile callback allowlists (`AllowWebsite`, `AllowMobileApp`).
3. Register providers explicitly; only configured providers are available.
4. Configure shared Data Protection key ring for multi-instance deployments.
5. Store secrets in a managed secret store (Key Vault, etc.), not in source.
6. Validate provider callback URLs (`/CloudLogin/Result`) in external IdP configuration.
7. Enable app monitoring for auth failures, 429s, and provider callback errors.
8. If using MAUI, ensure callback scheme matches `AllowMobileApp` on authority.

## Secure defaults

- Authentication cookies use `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, and the browser-enforced `__Host-` prefix.
- Session idle timeout is eight hours; persistent sign-in defaults to 30 days.
- Cookie tickets and consumer return state use ASP.NET Core Data Protection encryption and integrity protection.
- Authentication attempts and public account-discovery calls are rate limited per remote address.
- Passwords use versioned PBKDF2-HMAC-SHA256 with 600,000 iterations and a unique 128-bit salt. Valid older 100,000-iteration hashes are upgraded after the next successful login.
- New passwords accept passphrases and Unicode, require 12–128 characters, reject control characters, and support an application blocklist.
- Password hashes are never serialized into browser authentication tickets or API responses.
- Profile APIs enforce ownership; role, lock, credential-provider, and identifier fields remain server-managed.
- Profile images are size-limited, signature-checked, and restricted to PNG, JPEG, GIF, or WebP; active SVG uploads are rejected.
- Locked users are rejected consistently across password, test, OAuth handoff, and legacy login paths.
- Production startup rejects disabled HTTPS, unsafe redirect origins, test login, weak hash settings, invalid cookie settings, and browser-managed verification-code providers.
- Authentication endpoints send `no-store` responses and deny framing.

Customize only when required:

```csharp
builder.AddCloudLoginWeb(options =>
{
    options.AllowWebsite("https://app.example.com");

    options.Security.MinimumPasswordLength = 15;
    options.Security.AuthenticationPermitLimit = 5;
    options.Security.PasswordBlocklist.Add("company-name-2026");
    // Only needed when copying provider avatars into your own storage.
    options.Security.AllowedProfileImageHosts.Add("lh3.googleusercontent.com");
});
```

Security controls cannot be weakened below the enforced production minimums. Test mode
is disabled by default and is enabled in any environment only when its provider has
`IsEnabled` set to `true`. The deprecated browser-managed verification-code flow still
fails startup when explicitly configured because it cannot be made safe for production.

> **Production warning:** Test Mode deliberately signs in selected test accounts without
> external identity verification. When enabling it in Production, restrict access to the
> login deployment with network, gateway, or equivalent access controls and ensure test
> accounts have only the permissions required for testing.

## Production key and secret management

ASP.NET Core encrypts CloudLogin cookies and return state with Data Protection. A multi-instance deployment must persist its Data Protection key ring in a shared protected store. Configure this in the host before CloudLogin registration, for example with Azure Blob Storage plus Key Vault, Redis, or another supported shared key-ring provider.

```csharp
builder.Services
    .AddDataProtection()
    .SetApplicationName("Company.CloudLogin");
// Add the persistence and key-encryption provider used by your platform.
```

Operational requirements:

- Store OAuth secrets, Cosmos credentials, storage credentials, and Data Protection key-encryption keys in a managed secret store. Never commit them to `appsettings.json`.
- Enforce HTTPS at the edge and application, and enable HSTS in production.
- When running behind a reverse proxy, configure ASP.NET Core forwarded headers with an explicit trusted-proxy/network allowlist before authentication middleware; never trust forwarded headers from arbitrary clients.
- Enable Cosmos DB and storage encryption at rest; use customer-managed keys where required by organizational policy.
- Put internet-facing deployments behind a WAF/DDoS service. Application rate limiting is not a replacement for edge protection.
- Restrict health endpoints to non-sensitive status and monitor authentication failures, 429 responses, provider errors, and administrative actions without logging credentials, authorization codes, request IDs, or personal data.
- Back up the Data Protection key ring. Losing it invalidates active cookies; disclosing it can compromise protected payloads.

## Production startup troubleshooting

An IIS `500.30` response means the application failed before it could serve its normal
error page. Check Windows Event Viewer first. For a short diagnostic window, enable the
ASP.NET Core Module stdout log in the published `web.config` and ensure the application
pool identity can write to the selected folder:

```xml
<aspNetCore processPath="dotnet"
            arguments=".\Your.Login.dll"
            stdoutLogEnabled="true"
            stdoutLogFile=".\logs\stdout"
            hostingModel="inprocess" />
```

Disable stdout logging again after capturing the startup exception; logs can grow without
limit and may contain deployment details. An enabled test-mode provider is supported in
Development, Staging, and Production and no longer causes a startup failure.

## Migration notes

The secure defaults intentionally tighten previous behavior:

- Default cookies are now `__Host-CloudLogin` and `__Host-CloudLogin.Consumer`; existing sessions will sign in again once.
- Password registration now requires at least 12 characters. Existing passwords continue to work and their hashes upgrade after successful authentication.
- Test mode is disabled by default. When explicitly enabled, it is available in
  Development, Staging, and Production.
- Client-managed email/WhatsApp verification codes are disabled. The former flow exposed verification state to browser code and is not suitable for production authentication.
- Shared parent-domain cookies are no longer inferred from `BaseAddress`. Prefer coordinated login/logout. If a shared cookie is explicitly required, set `CookieDomain` and use a cookie name without the `__Host-` prefix after completing a subdomain threat review.

## Security scope

These controls provide a strong framework baseline, not automatic regulatory certification. Enterprise deployment still requires threat modeling, dependency and secret scanning, infrastructure hardening, centralized audit logging, incident response, penetration testing, privacy review, and controls appropriate to the required assurance level.

## Additional guidance

- [ASP.NET Core Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/)
- [ASP.NET Core rate limiting](https://learn.microsoft.com/aspnet/core/performance/rate-limit)
- [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [NIST SP 800-63B](https://pages.nist.gov/800-63-4/sp800-63b.html)

CloudLogin is part of the [Angry Monkey Cloud](https://angrymonkeycloud.com) ecosystem and is licensed under the [MIT License](LICENSE).

## External integrations and webhooks

CloudLogin remains authoritative for identity, users, workspaces, memberships, permissions, subscriptions, and billing data. Applications such as CDM consume those entities through the authenticated service API and retain only their own application data plus stable CloudLogin identifiers.

The service API exposes Workspace, User, Subscription, and Workspace-member reads for trusted server applications. Service keys belong on the server and must never be sent to WebAssembly or browser code.

The account page is real, path-based routing rather than a client-side tab switch: `/Account/Profile`, `/Account/Subscriptions`, and `/Account/Workspace/{workspaceId}` (optionally followed by `/Subscriptions` or `/PaymentMethods`) are each a distinct, refreshable, bookmarkable URL. A workspace isn't a tab in the personal account — it's a separate workspace with its own information, billing, members, and plans, reached through the account switcher in the rail; moving into or out of one is a full navigation, the same as switching accounts. Older integrations that built links with `?Section=` and `?EntityId=` (including `Section=Workspaces`) continue to work — CloudLogin translates them to the equivalent path on first load — but new integrations should link to the path directly. A host that mounts the account page somewhere other than `/Account` passes that path once via `AccountPageComponent.BasePath`.

### Generic webhook registrations

Webhook delivery is application-neutral. Configure registrations on `CloudLoginWebConfiguration.Webhooks`:

```csharp
new CloudLoginWebhookRegistration
{
    Application = "CDM",
    Url = new Uri("https://cdm.example/api/cloudlogin/webhook"),
    Secret = "<at least 32 characters>",
    Events =
    [
        "User.Updated",
        "Workspace.Updated",
        "Subscription.Updated",
        "Subscription.Cancelled"
    ]
}
```

An empty event set subscribes to every published event. Production webhook URLs must use HTTPS. Development HTTP endpoints are accepted only for local development. Configuration validation rejects missing application names, invalid URLs, short secrets, and blank event names.

CloudLogin mutation services publish a `CloudLoginEvent` through `ICloudLoginEventPublisher` after successful User, Workspace, membership, invitation, and Subscription persistence. Current events include `User.Created`, `User.Updated`, `User.Deleted`, `Workspace.Created`, `Workspace.Updated`, `Workspace.MembershipUpdated`, `Workspace.InvitationCreated`, `Workspace.Deleted`, `Subscription.Created`, `Subscription.Updated`, `Subscription.Cancelled`, and `Subscription.Deleted`. This remains application-neutral so CDM, Coverbox, Melon Cut, or any future application can consume the same stream. Events contain `EventId`, `EventType`, `EntityType`, `EntityId`, `Timestamp`, `Version`, `Operation`, and a JSON `Payload`.

Delivery signs the exact JSON body with HMAC-SHA256 and sends:

- `X-CloudLogin-Event-Id`
- `X-CloudLogin-Timestamp`
- `X-CloudLogin-Signature: sha256=<hex digest>`

Consumers must validate the signature against their registration secret, reject stale timestamps according to their security policy, and store successful event IDs for idempotency. `CloudLoginWebhookPublisher.Verify` provides constant-time signature comparison. Delivery retries transient failures four times with bounded backoff and reports a terminal error instead of using fire-and-forget delivery.

CloudLogin never creates application records in response to its own events. A consumer decides whether a linked record exists, which configured fields it synchronizes, and how it records delivery or synchronization errors. In particular, CDM only updates already-linked records and does not create a Business, Contact, or Subscription merely because CloudLogin emits an event.

### CDM mapping

The initial native CDM mappings are Business ↔ Workspace, Contact ↔ User, and Subscription ↔ Subscription. CDM can operate in a CloudLogin read-only view mode or a combined CloudLogin + CDM mode. CloudLogin-created lifecycle timestamps remain distinct from CDM's link time and actor. Manual Sync and record-specific Open in CloudLogin actions are provided by CDM's generic external-provider runtime.

