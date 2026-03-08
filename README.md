# Cloud Login Integration Guide

This document summarizes how Cloud Login is integrated into the Coverbox Fix solution and how to add it to other projects for authentication and user management.

## Table of Contents
1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [Required Packages / References](#3-required-packages--references)
4. [Configuration](#4-configuration)
5. [Web App Bootstrap](#5-web-app-bootstrap)
6. [Authentication Providers](#6-authentication-providers)
7. [Cosmos DB Configuration](#7-cosmos-db-configuration)
8. [Storage Configuration](#8-storage-configuration)
9. [Consuming From Other Projects](#9-consuming-from-other-projects)
10. [Client-Side Integration](#10-client-side-integration)
11. [User Management](#11-user-management)
12. [Session Management](#12-session-management)
13. [Role-Based Authorization](#13-role-based-authorization)
14. [Custom Claims & Profile Data](#14-custom-claims--profile-data)
15. [Security Best Practices](#15-security-best-practices)
16. [Blazor Integration](#16-blazor-integration)
17. [API Integration](#17-api-integration)
18. [Troubleshooting](#18-troubleshooting)
19. [Migration & Legacy Support](#19-migration--legacy-support)
20. [Production Deployment](#20-production-deployment)

## 1. Overview

Cloud Login provides comprehensive authentication and user management services including:
- **Multi-Provider Authentication**: Microsoft, Google, Facebook, Twitter, and custom providers
- **User Profile Management**: Store and retrieve user data in Cosmos DB
- **Avatar Storage**: Azure Blob Storage for user profile images
- **Session Management**: Secure token-based authentication
- **Role-Based Access Control**: Fine-grained permissions
- **Legacy Schema Support**: Backward compatibility with existing systems

### Key Components
- **`CloudLoginWeb`**: Full-featured login web application with UI
- **`CloudLoginClient`**: Client SDK for consuming authentication services
- **`CloudLogin.Server`**: Server-side helpers and middleware
- **`CloudLogin.DataContract`**: Shared models and contracts
- **`CloudLogin.Web.Components`**: Reusable Blazor UI components
- **`CloudLogin.Web.WASM`**: WebAssembly-specific components

## 2. Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    User Applications                          │
│        (Portal, API, Console, Mobile Apps)                    │
└────────────────────┬─────────────────────────────────────────┘
                     │
                     ▼
┌──────────────────────────────────────────────────────────────┐
│               CloudLogin Client SDK                           │
│          (Authentication, Profile Access)                     │
└────────────────────┬─────────────────────────────────────────┘
                     │
                     ▼
┌──────────────────────────────────────────────────────────────┐
│              CloudLogin Web Service                           │
│     (Coverbox.Login or Standalone Service)                    │
│  - OAuth Flow   - Token Management   - Profile CRUD           │
└────────────────────┬─────────────────────────────────────────┘
                     │
                     ├──────────────┬──────────────┐
                     ▼              ▼              ▼
┌──────────────┐  ┌─────────────┐  ┌──────────────┐
│ Auth Provider│  │  Cosmos DB  │  │Azure Storage │
│ (MS/Google)  │  │(User Data)  │  │ (Avatars)    │
└──────────────┘  └─────────────┘  └──────────────┘
```

### Project Structure
```
Coverbox.Login/
├── Coverbox.Login/                    # Main login web app
│   └── Program.cs                     # Bootstrap & configuration
├── Coverbox.Login.Client/             # Client-side components
└── appsettings.json                   # Configuration

External Dependencies (NuGet):
├── CloudLogin.Web                     # Web app framework
├── CloudLogin.Server                  # Server helpers
├── CloudLogin.Client                  # Client SDK
├── CloudLogin.DataContract            # Shared models
└── CloudLogin.Web.Components          # UI components
```

## 3. Required Packages / References

### Login Service (Standalone)
```xml
<ItemGroup>
  <PackageReference Include="AngryMonkey.CloudLogin.Web" Version="..." />
  <PackageReference Include="AngryMonkey.CloudLogin.Server" Version="..." />
  <PackageReference Include="AngryMonkey.CloudLogin.DataContract" Version="..." />
  <PackageReference Include="AngryMonkey.CloudLogin.Web.Components" Version="..." />
</ItemGroup>
```

### Consumer Applications (Portal, API)
```xml
<ItemGroup>
  <PackageReference Include="AngryMonkey.CloudLogin.Client" Version="..." />
  <PackageReference Include="AngryMonkey.CloudLogin.DataContract" Version="..." />
</ItemGroup>
```

### Blazor WebAssembly Integration
```xml
<ItemGroup>
  <PackageReference Include="AngryMonkey.CloudLogin.Client" Version="..." />
  <PackageReference Include="AngryMonkey.CloudLogin.Web.WASM" Version="..." />
</ItemGroup>
```

## 4. Configuration

### Complete appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "AngryMonkey.CloudLogin": "Debug"
    }
  },
  "AllowedHosts": "*",
  
  "Cosmos": {
    "AccountEndpoint": "https://yourcosmosdb.documents.azure.com:443/",
    "AccountKey": "<your-cosmos-key>",
    "Database": "CoverboxLogin",
    "Container": "Users",
    "PartitionKeyPath": "/PartitionKey",
    "ThroughputRUs": 400
  },
  
  "Storage": {
    "ConnectionString": "<your-storage-connection-string>",
    "Container": "user-avatars",
    "BaseUrl": "https://yourstorageaccount.blob.core.windows.net/"
  },
  
  "Microsoft": {
    "ClientId": "<azure-ad-app-client-id>",
    "ClientSecret": "<azure-ad-app-client-secret>",
    "TenantId": "common",
    "CallbackPath": "/signin-microsoft",
    "Scopes": ["openid", "profile", "email"]
  },
  
  "Google": {
    "ClientId": "<google-oauth-client-id>.apps.googleusercontent.com",
    "ClientSecret": "<google-oauth-client-secret>",
    "CallbackPath": "/signin-google",
    "Scopes": ["openid", "profile", "email"]
  },
  
  "Facebook": {
    "AppId": "<facebook-app-id>",
    "AppSecret": "<facebook-app-secret>",
    "CallbackPath": "/signin-facebook",
    "Scopes": ["email", "public_profile"]
  },
  
  "Security": {
    "JwtSecret": "<your-jwt-secret-key-at-least-32-chars>",
    "JwtIssuer": "https://login.coverbox.com",
    "JwtAudience": "https://portal.coverbox.com",
    "TokenExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 30,
    "RequireEmailVerification": false,
    "AllowMultipleSessions": true
  },
  
  "Features": {
    "EnableGoogleLogin": true,
    "EnableMicrosoftLogin": true,
    "EnableFacebookLogin": false,
    "EnableUserRegistration": true,
    "EnableProfileEditing": true,
    "EnableAvatarUpload": true,
    "MaxAvatarSizeMB": 5
  }
}
```

### Environment-Specific Configuration
```json
// appsettings.Development.json
{
  "Cosmos": {
    "AccountEndpoint": "https://localhost:8081",  // Cosmos Emulator
    "AccountKey": "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
  },
  "Storage": {
    "ConnectionString": "UseDevelopmentStorage=true"  // Storage Emulator
  },
  "Security": {
    "JwtSecret": "development-secret-key-do-not-use-in-production-min-32-chars",
    "RequireEmailVerification": false
  }
}

// appsettings.Production.json
{
  "Security": {
    "RequireEmailVerification": true,
    "AllowMultipleSessions": false
  },
  "Features": {
    "EnableUserRegistration": false  // Invite-only in production
  }
}
```

## 5. Web App Bootstrap

### Complete Program.cs Implementation
```csharp
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Providers;

var builder = WebApplication.CreateBuilder(args);

// Add services to container
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Configure CloudLogin
builder.AddCloudLoginWeb(new CloudLoginConfiguration()
{
    // Web UI Configuration
    WebConfig = config =>
    {
        config.PageDefaults.SetTitle("Coverbox Login");
        config.PageDefaults.SetFavicon("favicon.ico");
        config.Theme.PrimaryColor = "#0c75c4";
        config.Theme.SecondaryColor = "#10b981";
        config.RedirectAfterLogin = "https://portal.coverbox.com/";
        config.RedirectAfterLogout = "https://coverbox.com/";
    },
    
    // Cosmos DB Configuration
    Cosmos = new CosmosConfiguration(builder.Configuration.GetSection("Cosmos"))
    {
        // Partition key for user data (critical for query performance)
        UserInfoPartitionKeyValue = "User",
        
        // Legacy schema support (uppercase ID, PartitionKey, Discriminator)
        IncludeLegacySchema = true,
        
        // ID format: "user|{guid}" for backward compatibility
        SaveIdMode = IdSaveMode.TypePrefixed,
        
        // Auto-create database and container if missing
        AutoCreateDatabaseAndContainer = true,
        
        // Index policy optimization
        IndexingPolicy = new IndexingPolicy
        {
            Automatic = true,
            IndexingMode = IndexingMode.Consistent,
            IncludedPaths = 
            {
                new IncludedPath { Path = "/*" }
            },
            ExcludedPaths = 
            {
                new ExcludedPath { Path = "/Avatar/*" },
                new ExcludedPath { Path = "/LargeData/*" }
            }
        }
    },
    
    // Azure Storage Configuration
    AzureStorage = new StorageConfiguration(builder.Configuration.GetSection("Storage"))
    {
        Container = "user-avatars",
        CreateContainerIfNotExists = true,
        PublicAccessLevel = PublicAccessLevel.Blob,
        AllowedFileExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" },
        MaxFileSizeMB = 5
    },
    
    // Authentication Providers
    Providers = 
    [
        // Microsoft (Azure AD / Entra ID)
        new LoginProviders.MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft"))
        {
            DisplayName = "Microsoft Account",
            Icon = "microsoft.svg",
            ButtonColor = "#00A4EF",
            Order = 1
        },
        
        // Google
        new LoginProviders.GoogleProviderConfiguration(builder.Configuration.GetSection("Google"))
        {
            DisplayName = "Google",
            Icon = "google.svg",
            ButtonColor = "#DB4437",
            Order = 2
        },
        
        // Facebook (optional)
        // new LoginProviders.FacebookProviderConfiguration(builder.Configuration.GetSection("Facebook"))
    ],
    
    // Security Settings
    Security = new SecurityConfiguration
    {
        JwtSecret = builder.Configuration["Security:JwtSecret"]!,
        JwtIssuer = builder.Configuration["Security:JwtIssuer"]!,
        JwtAudience = builder.Configuration["Security:JwtAudience"]!,
        TokenExpirationMinutes = builder.Configuration.GetValue<int>("Security:TokenExpirationMinutes", 60),
        RefreshTokenExpirationDays = builder.Configuration.GetValue<int>("Security:RefreshTokenExpirationDays", 30),
        RequireHttps = builder.Environment.IsProduction(),
        CookieSecurePolicy = builder.Environment.IsProduction() 
            ? CookieSecurePolicy.Always 
            : CookieSecurePolicy.None
    },
    
    // Feature Flags
    Features = new FeatureConfiguration
    {
        EnableUserRegistration = builder.Configuration.GetValue<bool>("Features:EnableUserRegistration", true),
        EnableProfileEditing = builder.Configuration.GetValue<bool>("Features:EnableProfileEditing", true),
        EnableAvatarUpload = builder.Configuration.GetValue<bool>("Features:EnableAvatarUpload", true)
    },
    
    // Event Handlers (optional)
    Events = new LoginEvents
    {
        OnUserCreated = async (user, provider) =>
        {
            Console.WriteLine($"New user created: {user.Email} via {provider}");
            // Send welcome email, trigger analytics, etc.
        },
        
        OnUserLoggedIn = async (user, provider) =>
        {
            Console.WriteLine($"User logged in: {user.Email} via {provider}");
            // Update last login timestamp, log analytics
        },
        
        OnUserLoggedOut = async (user) =>
        {
            Console.WriteLine($"User logged out: {user.Email}");
        },
        
        OnProfileUpdated = async (user, changes) =>
        {
            Console.WriteLine($"Profile updated: {user.Email}");
            // Sync with external systems, trigger webhooks
        }
    }
});

// Initialize CloudLogin application
await CloudLoginWeb.InitApp(builder);
```

### Minimal Program.cs (Simplified)
```csharp
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Server.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.AddCloudLoginWeb(new CloudLoginConfiguration()
{
    WebConfig = config => config.PageDefaults.SetTitle("Coverbox Login"),
    Cosmos = new(builder.Configuration.GetSection("Cosmos"))
    {
        UserInfoPartitionKeyValue = "User",
        IncludeLegacySchema = true,
        SaveIdMode = IdSaveMode.TypePrefixed
    },
    AzureStorage = new(builder.Configuration.GetSection("Storage")),
    Providers = 
    [
        new LoginProviders.MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft")),
        new LoginProviders.GoogleProviderConfiguration(builder.Configuration.GetSection("Google"))
    ]
});

await CloudLoginWeb.InitApp(builder);
```

## 6. Authentication Providers

### Microsoft (Azure AD / Entra ID) Setup

#### Azure Portal Configuration
1. Navigate to **Azure Active Directory** → **App Registrations**
2. Click **New registration**
3. Set **Name**: "Coverbox Login"
4. Set **Redirect URI**: 
   - Type: `Web`
   - URI: `https://login.coverbox.com/signin-microsoft`
5. Click **Register**
6. Note the **Application (client) ID**
7. Go to **Certificates & secrets** → **New client secret**
8. Copy the secret value (only shown once!)
9. Go to **API permissions** → **Add permission** → **Microsoft Graph**
   - Add: `openid`, `profile`, `email`
10. Click **Grant admin consent**

#### Configuration
```json
{
  "Microsoft": {
    "ClientId": "12345678-1234-1234-1234-123456789abc",
    "ClientSecret": "your-client-secret-value-here",
    "TenantId": "common",  // or specific tenant GUID
    "CallbackPath": "/signin-microsoft"
  }
}
```

### Google OAuth Setup

#### Google Cloud Console Configuration
1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create new project or select existing
3. Navigate to **APIs & Services** → **Credentials**
4. Click **Create Credentials** → **OAuth client ID**
5. Configure consent screen if prompted
6. Application type: **Web application**
7. Add **Authorized redirect URIs**:
   - `https://login.coverbox.com/signin-google`
   - `http://localhost:5000/signin-google` (for development)
8. Copy **Client ID** and **Client Secret**

#### Configuration
```json
{
  "Google": {
    "ClientId": "123456789012-abcdefghijklmnopqrstuvwxyz123456.apps.googleusercontent.com",
    "ClientSecret": "GOCSPX-AbCdEfGhIjKlMnOpQrStUvWxYz",
    "CallbackPath": "/signin-google"
  }
}
```

### Facebook Login Setup

#### Facebook Developer Portal
1. Go to [Facebook for Developers](https://developers.facebook.com/)
2. Create new app → **Consumer** type
3. Add **Facebook Login** product
4. Configure **Valid OAuth Redirect URIs**: `https://login.coverbox.com/signin-facebook`
5. Copy **App ID** and **App Secret**

#### Configuration
```json
{
  "Facebook": {
    "AppId": "1234567890123456",
    "AppSecret": "abcdef0123456789abcdef0123456789",
    "CallbackPath": "/signin-facebook"
  }
}
```

### Custom Provider Implementation
```csharp
public class CustomOAuthProvider : IAuthenticationProvider
{
    public string Name => "CustomProvider";
    public string DisplayName => "Custom OAuth";
    
    public async Task<UserInfo> AuthenticateAsync(string code, string redirectUri)
    {
        // Exchange code for access token
        var tokenResponse = await ExchangeCodeForTokenAsync(code, redirectUri);
        
        // Get user info from provider API
        var userInfo = await GetUserInfoAsync(tokenResponse.AccessToken);
        
        // Map to CloudLogin user model
        return new UserInfo
        {
            Email = userInfo.Email,
            FirstName = userInfo.FirstName,
            LastName = userInfo.LastName,
            ProviderId = userInfo.Id,
            Provider = Name,
            AvatarUrl = userInfo.AvatarUrl
        };
    }
}

// Register custom provider
builder.AddCloudLoginWeb(new CloudLoginConfiguration()
{
    Providers = 
    [
        new CustomOAuthProvider()
    ]
});
```

## 7. Cosmos DB Configuration

### Schema Design

#### User Document Structure
```json
{
  "id": "user|12345678-1234-1234-1234-123456789abc",
  "ID": "12345678-1234-1234-1234-123456789abc",  // Legacy
  "PartitionKey": "User",  // Legacy
  "Discriminator": "UserInfo",  // Legacy
  "Type": "UserInfo",
  "Email": "user@example.com",
  "FirstName": "John",
  "LastName": "Doe",
  "DisplayName": "John Doe",
  "AvatarUrl": "https://storage.blob.core.windows.net/avatars/user123.jpg",
  "Provider": "Microsoft",
  "ProviderId": "azure-ad-object-id",
  "Roles": ["User", "Vendor"],
  "CustomData": {
    "PhoneNumber": "+1234567890",
    "Country": "LB",
    "PreferredLanguage": "ar"
  },
  "CreatedDate": "2024-01-15T10:30:00Z",
  "LastLoginDate": "2024-01-20T14:45:00Z",
  "IsActive": true,
  "IsEmailVerified": true
}
```

#### Legacy Schema Support
```csharp
Cosmos = new CosmosConfiguration(builder.Configuration.GetSection("Cosmos"))
{
    // Keep both old (uppercase) and new (lowercase) ID fields
    IncludeLegacySchema = true,
    
    // Save lowercase 'id' as "user|{guid}" instead of just "{guid}"
    SaveIdMode = IdSaveMode.TypePrefixed,
    
    // Maintain PartitionKey field for backward compatibility
    UserInfoPartitionKeyValue = "User"
}
```

### Indexing Policy
```csharp
new IndexingPolicy
{
    Automatic = true,
    IndexingMode = IndexingMode.Consistent,
    
    // Index all fields by default
    IncludedPaths = 
    {
        new IncludedPath { Path = "/*" }
    },
    
    // Exclude large fields from indexing
    ExcludedPaths = 
    {
        new ExcludedPath { Path = "/Avatar/*" },
        new ExcludedPath { Path = "/CustomData/LargeBlob/*" }
    },
    
    // Composite indexes for common queries
    CompositeIndexes =
    {
        new Collection<CompositePath>
        {
            new() { Path = "/Email", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/IsActive", Order = CompositePathSortOrder.Ascending }
        }
    }
}
```

### Partition Strategy
```
Partition Key: /PartitionKey (value: "User")
- All user documents in single partition
- Works well for < 100K users
- Consider user segmentation (by country, tenant) for larger scale
```

## 8. Storage Configuration

### Container Setup
```csharp
AzureStorage = new StorageConfiguration(builder.Configuration.GetSection("Storage"))
{
    Container = "user-avatars",
    CreateContainerIfNotExists = true,
    
    // Public read access for avatars
    PublicAccessLevel = PublicAccessLevel.Blob,
    
    // File restrictions
    AllowedFileExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" },
    MaxFileSizeMB = 5,
    
    // CORS for browser uploads
    CorsRules = new[]
    {
        new CorsRule
        {
            AllowedOrigins = new[] { "https://portal.coverbox.com", "https://coverbox.com" },
            AllowedMethods = new[] { "GET", "POST", "PUT" },
            AllowedHeaders = new[] { "*" },
            MaxAgeInSeconds = 3600
        }
    }
}
```

### Avatar Upload Flow
```csharp
public async Task<string> UploadAvatarAsync(Guid userId, Stream imageStream, string fileName)
{
    var containerClient = _storageClient.GetBlobContainerClient("user-avatars");
    
    // Generate unique blob name
    string extension = Path.GetExtension(fileName);
    string blobName = $"{userId}/{Guid.NewGuid()}{extension}";
    
    var blobClient = containerClient.GetBlobClient(blobName);
    
    // Upload with metadata
    await blobClient.UploadAsync(imageStream, new BlobUploadOptions
    {
        HttpHeaders = new BlobHttpHeaders
        {
            ContentType = GetContentType(extension)
        },
        Metadata = new Dictionary<string, string>
        {
            { "UserId", userId.ToString() },
            { "UploadDate", DateTime.UtcNow.ToString("O") }
        }
    });
    
    return blobClient.Uri.ToString();
}
```

## 9. Consuming From Other Projects

### Recommended .NET Integration Pattern

If `CloudLogin` is hosted as a separate login site and your application wants to keep its own app cookie/session, use this split:

1. a shared Blazor project that references `AngryMonkey.CloudLogin.Shared`
2. an ASP.NET Core host that owns the cookie and callback endpoints
3. a web or MAUI client that uses `ICloudLoginService`

### Step 1: Add the Required References

```xml
<!-- Shared Blazor/RCL project -->
<ItemGroup>
  <PackageReference Include="AngryMonkey.CloudLogin.Client" Version="..." />
  <PackageReference Include="AngryMonkey.CloudLogin.Shared" Version="..." />
</ItemGroup>

<!-- ASP.NET Core host project -->
<ItemGroup>
  <PackageReference Include="AngryMonkey.CloudLogin.API" Version="..." />
  <PackageReference Include="AngryMonkey.CloudLogin.Client" Version="..." />
</ItemGroup>

<!-- .NET MAUI Blazor project -->
<ItemGroup>
  <PackageReference Include="AngryMonkey.CloudLogin.AppService" Version="..." />
</ItemGroup>
```

### Step 2: Point Your App to the CloudLogin Site

```json
{
  "LoginUrl": "https://login2.coverbox.app"
}
```

`LoginUrl` must be the base address of the hosted `CloudLogin` site. It is used for redirects and for calling `/CloudLogin/Request/GetUserByRequestId`.

### Step 3: Add the CloudLogin Route Assembly to Your Router

`AngryMonkey.CloudLogin.Shared` ships the built-in `/cloudlogin/login` route. Add its assembly to your app router:

```razor
<Router AppAssembly="typeof(Layout.MainLayout).Assembly"
        AdditionalAssemblies="AngryMonkey.CloudLogin.CloudLoginRouting.AdditionalAssemblies">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

### Step 4: Configure Cookie Authentication in the Host App

```csharp
using AngryMonkey.CloudLogin;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCloudLoginServer(new CloudLoginServerConfiguration
{
    CookieName = "MyAppAuth"
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

`AddCloudLoginServer` configures cookie auth with:

- login path: `/auth/login`
- logout path: `/auth/logout`
- access denied path: `/auth/login`
- a long-lived sliding cookie

### Step 5: Add an Auth Bridge Controller

Your host app should expose `/auth/login`, `/auth/callback`, and optionally `/auth/profile` and `/auth/logout`.

The pattern is:

1. build a callback URL in your app
2. redirect the browser to `LoginUrl` with `referer={callbackUrl}`
3. receive `requestId` from CloudLogin on the callback
4. call `GET {LoginUrl}/CloudLogin/Request/GetUserByRequestId?requestId=...`
5. map the returned `UserModel` into claims
6. sign your local cookie
7. redirect back to the original page and append `rid={requestId}`

Minimal shape:

```csharp
[ApiController]
[Route("auth")]
public class AuthController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        returnUrl ??= "/";

        string state = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(returnUrl));
        string? callbackUrl = Url.Action("Callback", "Auth", new { state }, Request.Scheme);

        string loginBaseUrl = configuration["LoginUrl"]!;
        string finalUrl = $"{loginBaseUrl}?referer={Uri.EscapeDataString(callbackUrl!)}";

        return Redirect(finalUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string requestId, [FromQuery] string? state)
    {
        UserModel? user = await GetUserFromCloudLogin(requestId);
        if (user == null)
            return Redirect("/?error=user_not_found");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ID.ToString()),
            new(ClaimTypes.Name, user.DisplayName ?? user.FirstName ?? "User"),
            new(ClaimTypes.Email, user.PrimaryEmailAddress?.Input ?? string.Empty),
            new("CloudLoginRequestId", requestId)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        string returnUrl = string.IsNullOrEmpty(state)
            ? "/"
            : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(state));

        return Redirect($"{returnUrl}{(returnUrl.Contains('?') ? '&' : '?')}rid={Uri.EscapeDataString(requestId)}");
    }

    private async Task<UserModel?> GetUserFromCloudLogin(string requestId)
    {
        using HttpClient httpClient = new() { BaseAddress = new Uri(configuration["LoginUrl"]!) };
        return await httpClient.GetFromJsonAsync<UserModel>($"/CloudLogin/Request/GetUserByRequestId?requestId={Uri.EscapeDataString(requestId)}");
    }
}
```

### Step 6: Expose Current-User and Logout Endpoints for the Browser Client

`CloudLoginWebService` expects your site to provide these endpoints:

- `GET /api/users/getUser`
- `GET /api/users/logout`
- `POST /api/users/logout`

Minimal implementation:

```csharp
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    [HttpGet("getUser")]
    public IActionResult GetUser()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return Ok((UserModel?)null);

        string? userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
            return Ok((UserModel?)null);

        return Ok(new UserModel
        {
            ID = userId,
            DisplayName = User.FindFirst(ClaimTypes.Name)?.Value,
            FirstName = User.FindFirst(ClaimTypes.GivenName)?.Value,
            LastName = User.FindFirst(ClaimTypes.Surname)?.Value,
            ProfilePicture = User.FindFirst("picture")?.Value,
            Inputs = []
        });
    }

    [HttpGet("logout")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return Ok(new { success = true });
    }
}
```

### Step 7: Register the Browser-Side Service

In a Blazor WebAssembly client hosted by your ASP.NET Core app:

```csharp
builder.Services.AddScoped<ICloudLoginService, CloudLoginWebService>();
```

Warm it up during startup so cached user state is restored and then refreshed from `/api/users/getUser`:

```csharp
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var loginService = scope.ServiceProvider.GetRequiredService<ICloudLoginService>();

    if (loginService is CloudLoginWebService webLoginService)
    {
        _ = webLoginService.User;
        try { await webLoginService.RefreshUserAsync(); } catch { }
    }
}
```

### Step 8: Use `ICloudLoginService` from Components

```razor
@inject ICloudLoginService CloudLogin

@if (CloudLogin.User is null)
{
    <button @onclick="SignIn">Sign in</button>
}
else
{
    <button @onclick="SignOut">Sign out</button>
}

@code {
    private Task SignIn() => CloudLogin.Login();
    private Task SignOut() => CloudLogin.Logout();
}
```

`CloudLogin.Login()` navigates to the local `/cloudlogin/login` page first. That page then calls `BeginLoginAsync(returnUrl)` and completes the flow.

### End-to-End Browser Flow

1. user clicks sign-in in your app
2. `CloudLoginWebService.Login()` navigates to `/cloudlogin/login?returnUrl=...`
3. the shared sign-in page calls `BeginLoginAsync(...)`
4. `CloudLoginWebService.BeginLoginAsync(...)` force-loads `/auth/login?returnUrl=...`
5. your host app redirects to `LoginUrl?referer={callback}`
6. CloudLogin authenticates the user and redirects back with `requestId`
7. your callback endpoint fetches the user, signs your local cookie, and redirects back with `rid`
8. the shared sign-in page sees `rid`, stores it as `RequestId`, calls `FetchUser()`, and navigates back to the original page

### Portal Integration (Program.cs)
```csharp
using AngryMonkey.CloudLogin;

var builder = WebApplication.CreateBuilder(args);

// Register CloudLogin Client
CloudLoginClient cloudLogin = new() 
{ 
    HttpServer = new() 
    { 
        BaseAddress = new Uri(builder.Configuration["LoginUrl"]!) 
    } 
};

builder.Services.AddSingleton(cloudLogin);

// Use in services/domains
var app = builder.Build();

// Access from dependency injection
app.MapGet("/api/profile", async (CloudLoginClient loginClient) =>
{
    var user = await loginClient.GetCurrentUserAsync();
    return Results.Ok(user);
});
```

### API Integration
```csharp
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly CloudLoginClient _loginClient;

    public UserController(CloudLoginClient loginClient)
    {
        _loginClient = loginClient;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile([FromQuery] Guid userId)
    {
        var user = await _loginClient.GetUserAsync(userId);
        
        if (user == null)
            return NotFound();
            
        return Ok(user);
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var user = await _loginClient.UpdateUserAsync(request.UserId, new UserInfo
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            CustomData = request.CustomData
        });
        
        return Ok(user);
    }
}
```

## 10. Client-Side Integration

### .NET Client Notes

For .NET applications, prefer the `ICloudLoginService` pattern above over raw browser calls.

- `CloudLoginWebService` restores cached user data from `localStorage`
- `CloudLoginWebService.RefreshUserAsync()` calls `GET /api/users/getUser`
- `CloudLoginWebService.Logout()` calls `GET /api/users/logout`
- `CloudLoginBaseService.FetchUser()` calls `GET {LoginUrl}/CloudLogin/Request/GetUserByRequestId`
- the built-in sign-in route is `/cloudlogin/login`

The JavaScript and React examples below are lower-level alternatives when you are not using Blazor or .NET MAUI.

### JavaScript/TypeScript Client
```typescript
class CloudLoginClient {
    constructor(private baseUrl: string) {}
    
    async login(provider: 'microsoft' | 'google'): Promise<void> {
        window.location.href = `${this.baseUrl}/login/${provider}?returnUrl=${encodeURIComponent(window.location.href)}`;
    }
    
    async getCurrentUser(): Promise<UserInfo | null> {
        const response = await fetch(`${this.baseUrl}/api/user/current`, {
            credentials: 'include'
        });
        
        if (!response.ok) return null;
        return await response.json();
    }
    
    async logout(): Promise<void> {
        await fetch(`${this.baseUrl}/api/auth/logout`, {
            method: 'POST',
            credentials: 'include'
        });
        
        window.location.href = '/';
    }
}

// Usage
const loginClient = new CloudLoginClient('https://login.coverbox.com');
const user = await loginClient.getCurrentUser();

if (!user) {
    await loginClient.login('microsoft');
}
```

### React Hook
```tsx
import { useState, useEffect } from 'react';

interface User {
    id: string;
    email: string;
    displayName: string;
    avatarUrl?: string;
}

export function useAuth() {
    const [user, setUser] = useState<User | null>(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        fetchUser();
    }, []);

    async function fetchUser() {
        try {
            const response = await fetch('https://login.coverbox.com/api/user/current', {
                credentials: 'include'
            });
            
            if (response.ok) {
                const userData = await response.json();
                setUser(userData);
            }
        } catch (error) {
            console.error('Failed to fetch user:', error);
        } finally {
            setLoading(false);
        }
    }

    async function login(provider: 'microsoft' | 'google') {
        window.location.href = `https://login.coverbox.com/login/${provider}`;
    }

    async function logout() {
        await fetch('https://login.coverbox.com/api/auth/logout', {
            method: 'POST',
            credentials: 'include'
        });
        setUser(null);
        window.location.href = '/';
    }

    return { user, loading, login, logout };
}

// Usage in component
function App() {
    const { user, loading, login, logout } = useAuth();

    if (loading) return <div>Loading...</div>;

    if (!user) {
        return (
            <div>
                <button onClick={() => login('microsoft')}>Login with Microsoft</button>
                <button onClick={() => login('google')}>Login with Google</button>
            </div>
        );
    }

    return (
        <div>
            <img src={user.avatarUrl} alt={user.displayName} />
            <span>Welcome, {user.displayName}</span>
            <button onClick={logout}>Logout</button>
        </div>
    );
}
```

## 11. User Management

### Create User
```csharp
public async Task<UserInfo> CreateUserAsync(string email, string firstName, string lastName)
{
    var user = new UserInfo
    {
        Email = email,
        FirstName = firstName,
        LastName = lastName,
        DisplayName = $"{firstName} {lastName}",
        Provider = "Manual",
        IsActive = true,
        IsEmailVerified = false,
        CreatedDate = DateTime.UtcNow,
        Roles = new List<string> { "User" }
    };
    
    return await _cloudLoginClient.CreateUserAsync(user);
}
```

### Get User by ID
```csharp
var user = await _cloudLoginClient.GetUserAsync(userId);

if (user != null)
{
    Console.WriteLine($"User: {user.DisplayName}");
    Console.WriteLine($"Email: {user.Email}");
    Console.WriteLine($"Roles: {string.Join(", ", user.Roles)}");
}
```

### Update User Profile
```csharp
user.FirstName = "Jane";
user.CustomData["PhoneNumber"] = "+9611234567";
user.CustomData["Country"] = "LB";

var updatedUser = await _cloudLoginClient.UpdateUserAsync(user.ID, user);
```

### Delete User
```csharp
await _cloudLoginClient.DeleteUserAsync(userId);
```

### Search Users
```csharp
var users = await _cloudLoginClient.SearchUsersAsync(new UserSearchRequest
{
    Email = "user@example.com",
    Roles = new[] { "Admin", "VendorManager" },
    IsActive = true,
    Page = 1,
    PageSize = 50
});

foreach (var user in users.Results)
{
    Console.WriteLine($"{user.Email} - {string.Join(", ", user.Roles)}");
}
```

## 12. Session Management

### Token-Based Authentication
```csharp
// Generate token after login
var token = await _cloudLoginClient.GenerateTokenAsync(user.ID);

// Token structure
{
    "AccessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "RefreshToken": "abc123def456...",
    "ExpiresAt": "2024-01-20T15:30:00Z",
    "TokenType": "Bearer"
}

// Validate token
var principal = await _cloudLoginClient.ValidateTokenAsync(token.AccessToken);

if (principal != null)
{
    var userId = principal.FindFirst("sub")?.Value;
    var email = principal.FindFirst("email")?.Value;
}
```

### Cookie-Based Sessions
```csharp
// Configured in Program.cs
Security = new SecurityConfiguration
{
    CookieName = "CoverboxAuth",
    CookieSecurePolicy = CookieSecurePolicy.Always,
    CookieHttpOnly = true,
    CookieSameSite = SameSiteMode.Lax,
    CookieExpirationMinutes = 60
}

// Session stored in encrypted cookie
// Automatically validated on each request
```

### Multiple Session Management
```csharp
// Allow/deny multiple simultaneous sessions
Security = new SecurityConfiguration
{
    AllowMultipleSessions = false  // Logout other sessions on new login
}

// Get active sessions for user
var sessions = await _cloudLoginClient.GetUserSessionsAsync(userId);

// Revoke specific session
await _cloudLoginClient.RevokeSessionAsync(sessionId);

// Revoke all sessions (force logout everywhere)
await _cloudLoginClient.RevokeAllSessionsAsync(userId);
```

## 13. Role-Based Authorization

### Define Roles
```csharp
public static class Roles
{
    public const string Admin = "Admin";
    public const string VendorManager = "VendorManager";
    public const string Vendor = "Vendor";
    public const string User = "User";
    public const string Guest = "Guest";
}
```

### Assign Roles
```csharp
user.Roles = new List<string> { Roles.User, Roles.Vendor };
await _cloudLoginClient.UpdateUserAsync(user.ID, user);
```

### Check Permissions
```csharp
public async Task<bool> HasRoleAsync(Guid userId, string role)
{
    var user = await _cloudLoginClient.GetUserAsync(userId);
    return user?.Roles?.Contains(role) ?? false;
}

// Usage
if (await HasRoleAsync(userId, Roles.Admin))
{
    // Admin-only operations
}
```

### ASP.NET Core Authorization
```csharp
// Configure in Program.cs
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => 
        policy.RequireRole(Roles.Admin));
        
    options.AddPolicy("VendorOrAdmin", policy =>
        policy.RequireRole(Roles.Vendor, Roles.Admin));
});

// Use in controllers
[Authorize(Policy = "RequireAdmin")]
[HttpDelete("vendors/{id}")]
public async Task<IActionResult> DeleteVendor(Guid id)
{
    await _vendorService.DeleteAsync(id);
    return NoContent();
}
```

### Blazor Authorization
```razor
<AuthorizeView Roles="@Roles.Admin">
    <Authorized>
        <button @onclick="DeleteVendor">Delete</button>
    </Authorized>
    <NotAuthorized>
        <p>Admin access required</p>
    </NotAuthorized>
</AuthorizeView>

<AuthorizeView Policy="VendorOrAdmin">
    <Authorized>
        <EditVendorForm Vendor="@currentVendor" />
    </Authorized>
</AuthorizeView>
```

## 14. Custom Claims & Profile Data

### Store Custom Data
```csharp
user.CustomData = new Dictionary<string, object>
{
    { "PhoneNumber", "+9611234567" },
    { "Country", "LB" },
    { "PreferredLanguage", "ar" },
    { "VendorId", vendorId },
    { "Preferences", new { Theme = "dark", Notifications = true } },
    { "LastVisitedPage", "/vendors/123" }
};

await _cloudLoginClient.UpdateUserAsync(user.ID, user);
```

### Retrieve Custom Data
```csharp
var user = await _cloudLoginClient.GetUserAsync(userId);

if (user.CustomData.TryGetValue("PhoneNumber", out var phoneNumber))
{
    Console.WriteLine($"Phone: {phoneNumber}");
}

if (user.CustomData.TryGetValue("VendorId", out var vendorId))
{
    var vendor = await _vendorService.GetAsync((Guid)vendorId);
}
```

### Type-Safe Custom Data
```csharp
public class UserPreferences
{
    public string Theme { get; set; } = "light";
    public bool Notifications { get; set; } = true;
    public string PreferredLanguage { get; set; } = "en";
}

// Store
var preferences = new UserPreferences { Theme = "dark", PreferredLanguage = "ar" };
user.CustomData["Preferences"] = JsonSerializer.Serialize(preferences);
await _cloudLoginClient.UpdateUserAsync(user.ID, user);

// Retrieve
if (user.CustomData.TryGetValue("Preferences", out var prefJson))
{
    var preferences = JsonSerializer.Deserialize<UserPreferences>(prefJson.ToString()!);
}
```

## 15. Security Best Practices

### Environment Variables for Secrets
```bash
# Never commit secrets to source control
# Use environment variables or secret managers

export COSMOS_KEY="your-cosmos-key"
export STORAGE_CONNECTION="your-storage-connection"
export JWT_SECRET="your-jwt-secret-min-32-chars"
export MICROSOFT_CLIENT_SECRET="your-microsoft-secret"
export GOOGLE_CLIENT_SECRET="your-google-secret"
```

### Azure Key Vault Integration
```csharp
if (builder.Environment.IsProduction())
{
    var keyVaultEndpoint = new Uri(builder.Configuration["KeyVault:Endpoint"]!);
    builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, new DefaultAzureCredential());
}

// Secrets stored in Key Vault:
// - Cosmos--AccountKey
// - Storage--ConnectionString
// - Security--JwtSecret
// - Microsoft--ClientSecret
// - Google--ClientSecret
```

### HTTPS Enforcement
```csharp
if (builder.Environment.IsProduction())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

Security = new SecurityConfiguration
{
    RequireHttps = true,
    CookieSecurePolicy = CookieSecurePolicy.Always
}
```

### CORS Configuration
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowPortal", policy =>
    {
        policy.WithOrigins(
            "https://portal.coverbox.com",
            "https://coverbox.com"
        )
        .AllowCredentials()
        .AllowAnyMethod()
        .AllowAnyHeader();
    });
});

app.UseCors("AllowPortal");
```

### Token Rotation
```csharp
// Refresh tokens periodically
var newToken = await _cloudLoginClient.RefreshTokenAsync(currentToken.RefreshToken);

// Store new token
HttpContext.Response.Cookies.Append("AuthToken", newToken.AccessToken, new CookieOptions
{
    Secure = true,
    HttpOnly = true,
    SameSite = SameSiteMode.Lax,
    Expires = DateTimeOffset.UtcNow.AddMinutes(60)
});
```

## 16. Blazor Integration

### Shared Blazor Router

Always include the `AngryMonkey.CloudLogin.Shared` assembly in your router so the built-in `/cloudlogin/login` page is reachable:

```razor
<Router AppAssembly="typeof(Layout.MainLayout).Assembly"
        AdditionalAssemblies="AngryMonkey.CloudLogin.CloudLoginRouting.AdditionalAssemblies">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

The shared sign-in page:

- accepts `r` or `rid` from the callback URL
- accepts `returnUrl`
- calls `ICloudLoginService.BeginLoginAsync(...)`
- waits for `RequestId`
- calls `FetchUser()` once a request id is available
- navigates back to the original page when `User` is loaded

### Browser-Hosted Blazor

```csharp
builder.Services.AddScoped<ICloudLoginService, CloudLoginWebService>();
```

Use it directly from components:

```razor
@inject ICloudLoginService CloudLogin

@if (CloudLogin.User is null)
{
    <button @onclick="SignIn">Sign in</button>
}
else
{
    <button @onclick="OpenProfile">My profile</button>
    <button @onclick="SignOut">Sign out</button>
}

@code {
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private Task SignIn() => CloudLogin.Login();
    private Task SignOut() => CloudLogin.Logout();

    private async Task OpenProfile()
    {
        string profileUrl = await CloudLogin.ProfileUrl();
        Navigation.NavigateTo(profileUrl, forceLoad: true);
    }
}
```

### .NET MAUI Blazor Hybrid

Register the MAUI implementation:

```csharp
builder.Services.AddScoped<ICloudLoginService, CloudLoginAppService>();
```

Initialize it during app startup so stored sessions are restored before the main UI is shown:

```csharp
protected override Window CreateWindow(IActivationState? activationState)
{
    Window window = new(new LoadingPage());
    _ = InitializeAndShowMainPageAsync(window);
    return window;
}

private async Task InitializeAndShowMainPageAsync(Window window)
{
    ICloudLoginService loginService = _services.GetRequiredService<ICloudLoginService>();

    if (loginService is CloudLoginAppService mauiLoginService)
    {
        await mauiLoginService.InitializeAsync();

        if (loginService.User != null)
            _ = Task.Run(async () => await loginService.FetchUser());
    }

    MainThread.BeginInvokeOnMainThread(() => window.Page = new MainPage());
}
```

`CloudLoginAppService.BeginLoginAsync(...)` uses `WebAuthenticator.Default.AuthenticateAsync(...)`, passes `referer={callback}` to the hosted CloudLogin site, receives `requestId` from the native callback, and then uses `FetchUser()` to hydrate `User`.

`CloudLoginAppService` also persists the current session in `Preferences` and `SecureStorage`, so app restarts can restore the user before a fresh fetch.

## 17. API Integration

### Minimal API Endpoints
```csharp
app.MapGet("/api/auth/status", async (CloudLoginClient loginClient, HttpContext context) =>
{
    var userId = context.User.FindFirst("sub")?.Value;
    
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();
        
    var user = await loginClient.GetUserAsync(Guid.Parse(userId));
    
    return user != null ? Results.Ok(user) : Results.NotFound();
})
.RequireAuthorization();

app.MapPost("/api/auth/logout", async (CloudLoginClient loginClient, HttpContext context) =>
{
    var userId = context.User.FindFirst("sub")?.Value;
    
    if (!string.IsNullOrEmpty(userId))
    {
        await loginClient.LogoutAsync(Guid.Parse(userId));
    }
    
    context.Response.Cookies.Delete("AuthToken");
    return Results.Ok();
});
```

## 18. Troubleshooting

### Common Issues

**"401 Unauthorized" on API calls**
```csharp
// Ensure credentials are included
var response = await httpClient.GetAsync("https://login.coverbox.com/api/user/current", 
    new HttpRequestMessage 
    { 
        Options = { [new HttpRequestOptionsKey<bool>("WithCredentials")] = true }
    });
```

**"CORS policy blocking requests"**
```csharp
// Add CORS policy in CloudLogin service
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

**"User data not persisting"**
- Check Cosmos DB connection string
- Verify container name matches configuration
- Check partition key value ("User")
- Review indexing policy

**"Avatar upload fails"**
- Check storage connection string
- Verify container exists and has blob public access
- Check file size limits (default 5MB)
- Ensure file extension is allowed

### Logging & Diagnostics
```csharp
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Debug);
    logging.AddFilter("AngryMonkey.CloudLogin", LogLevel.Trace);
});

// Enable detailed Cosmos logging
builder.Services.Configure<CosmosClientOptions>(options =>
{
    options.EnableContentResponseOnWrite = true;
    options.AllowBulkExecution = false;  // Better error messages
});
```

## 19. Migration & Legacy Support

### Upgrade from Old Schema
```csharp
// Old schema (uppercase ID)
{
  "ID": "12345678-1234-1234-1234-123456789abc",
  "PartitionKey": "User",
  "Discriminator": "UserInfo"
}

// New schema (lowercase id with prefix)
{
  "id": "user|12345678-1234-1234-1234-123456789abc",
  "Type": "UserInfo"
}

// Support both during migration
Cosmos = new CosmosConfiguration(...)
{
    IncludeLegacySchema = true,  // Write both formats
    SaveIdMode = IdSaveMode.TypePrefixed  // id = "user|{guid}"
}
```

### Data Migration Script
```csharp
public async Task MigrateUsersAsync()
{
    var container = cosmosClient.GetContainer("CoverboxLogin", "Users");
    var query = "SELECT * FROM c WHERE c.Discriminator = 'UserInfo'";
    
    var iterator = container.GetItemQueryIterator<UserInfo>(query);
    
    while (iterator.HasMoreResults)
    {
        var batch = await iterator.ReadNextAsync();
        
        foreach (var user in batch)
        {
            // Update to new schema
            user.Type = "UserInfo";
            
            await container.UpsertItemAsync(user, new PartitionKey("User"));
            Console.WriteLine($"Migrated user: {user.Email}");
        }
    }
}
```

## 20. Production Deployment

### Azure App Service
```bash
# Deploy CloudLogin service
az webapp create --resource-group CoverboxRG --plan CoverboxPlan --name coverbox-login
az webapp config appsettings set --resource-group CoverboxRG --name coverbox-login \
  --settings @appsettings.Production.json

# Enable HTTPS only
az webapp update --resource-group CoverboxRG --name coverbox-login --https-only true

# Configure custom domain
az webapp config hostname add --resource-group CoverboxRG --webapp-name coverbox-login \
  --hostname login.coverbox.com
```

### Docker Deployment
```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["Coverbox.Login/Coverbox.Login.csproj", "Coverbox.Login/"]
RUN dotnet restore "Coverbox.Login/Coverbox.Login.csproj"
COPY . .
WORKDIR "/src/Coverbox.Login"
RUN dotnet build "Coverbox.Login.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Coverbox.Login.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Coverbox.Login.dll"]
```

```bash
# Build and run
docker build -t coverbox-login .
docker run -d -p 443:443 \
  -e Cosmos__AccountKey=$COSMOS_KEY \
  -e Storage__ConnectionString=$STORAGE_CONN \
  coverbox-login
```

### Health Checks
```csharp
builder.Services.AddHealthChecks()
    .AddCosmosDb(
        connectionString: builder.Configuration["Cosmos:ConnectionString"]!,
        database: builder.Configuration["Cosmos:Database"]!)
    .AddAzureBlobStorage(
        connectionString: builder.Configuration["Storage:ConnectionString"]!);

app.MapHealthChecks("/health");
```

### Monitoring
```csharp
// Application Insights
builder.Services.AddApplicationInsightsTelemetry(builder.Configuration["ApplicationInsights:ConnectionString"]);

// Custom metrics
var telemetryClient = app.Services.GetRequiredService<TelemetryClient>();

Events.OnUserLoggedIn += async (user, provider) =>
{
    telemetryClient.TrackEvent("UserLogin", new Dictionary<string, string>
    {
        { "Provider", provider },
        { "UserId", user.ID.ToString() }
    });
};
```

---

## Additional Resources
- CloudLogin Repository: https://github.com/angrymonkeycloud/CloudLogin
- Azure AD Documentation: https://docs.microsoft.com/azure/active-directory/
- Google OAuth Documentation: https://developers.google.com/identity/protocols/oauth2
- Sample Implementation: Review `Coverbox.Login` project structure

---
*Last Updated: 2025*
*Version: 1.0*
