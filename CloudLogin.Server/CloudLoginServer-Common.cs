using System.Web;
using System.Security.Claims;
using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using System.Text.RegularExpressions;
using AngryMonkey.CloudLogin.Sever.Providers;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer : ICloudLogin
{
    // URL Generation methods for login flows
    /// <summary>
    /// Generates a login URL for web applications
    /// </summary>
    /// <param name="referer">The external website URL that referred to CloudLogin</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <returns>The complete login URL</returns>
    public string GetLoginUrl(string? referer = null, bool isMobileApp = false)
    {
        string baseUrl = LoginUrl.TrimEnd('/');

        List<string> parameters = [];

        if (!string.IsNullOrEmpty(referer))
            parameters.Add($"referer={HttpUtility.UrlEncode(referer)}");

        if (isMobileApp)
            parameters.Add("isMobileApp=true");

        string queryString = parameters.Count > 0 ? "?" + string.Join("&", parameters) : "";
        return $"{baseUrl}/{queryString}";
    }

    /// <summary>
    /// Generates a login URL for external provider authentication
    /// </summary>
    /// <param name="providerCode">The provider code (e.g., "google", "microsoft")</param>
    /// <param name="referer">The external website URL that referred to CloudLogin (legacy parameter name)</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <param name="keepMeSignedIn">Whether to maintain persistent session</param>
    /// <param name="finalReferer">The external website URL that referred to CloudLogin</param>
    /// <returns>The complete provider login URL</returns>
    public string GetProviderLoginUrl(string providerCode, string? referer = null, bool isMobileApp = false, bool keepMeSignedIn = false)
    {
        if (string.IsNullOrEmpty(providerCode))
            throw new ArgumentException("Provider code cannot be null or empty", nameof(providerCode));

        string baseUrl = LoginUrl.TrimEnd('/');
        referer ??= "/";

        List<string> parameters = [];

        if (!string.IsNullOrEmpty(referer))
            parameters.Add($"referer={HttpUtility.UrlEncode(referer)}");

        if (isMobileApp)
            parameters.Add("isMobileApp=true");

        if (keepMeSignedIn)
            parameters.Add("keepMeSignedIn=true");

        string queryString = parameters.Count > 0 ? "?" + string.Join("&", parameters) : "";
        return $"{baseUrl}/cloudlogin/login/{providerCode.ToLowerInvariant()}{queryString}";
    }

    /// <summary>
    /// Generates a custom login URL with additional parameters
    /// </summary>
    /// <param name="referer">The external website URL that referred to CloudLogin (legacy parameter name)</param>
    /// <param name="isMobileApp">Indicates if this is for a mobile application</param>
    /// <param name="keepMeSignedIn">Whether to maintain persistent session</param>
    /// <param name="userHint">Optional user hint (email/phone)</param>
    /// <param name="finalReferer">The external website URL that referred to CloudLogin</param>
    /// <returns>The complete custom login URL</returns>
    public string GetCustomLoginUrl(string? referer = null, bool isMobileApp = false, bool keepMeSignedIn = false, string? userHint = null)
    {
        string baseUrl = LoginUrl.TrimEnd('/');
        referer ??= "/";

        List<string> parameters = [];

        if (!string.IsNullOrEmpty(referer))
            parameters.Add($"referer={HttpUtility.UrlEncode(referer)}");

        if (isMobileApp)
            parameters.Add("isMobileApp=true");

        if (keepMeSignedIn)
            parameters.Add("keepMeSignedIn=true");

        if (!string.IsNullOrEmpty(userHint))
            parameters.Add($"input={HttpUtility.UrlEncode(userHint)}");

        string queryString = parameters.Count > 0 ? "?" + string.Join("&", parameters) : "";
        return $"{baseUrl}/cloudlogin/login{queryString}";
    }

    public CloudLoginInputFormat GetInputFormat(string input)
    {
        if (string.IsNullOrEmpty(input))
            return CloudLoginInputFormat.Other;

        if (IsInputValidEmailAddress(input))
            return CloudLoginInputFormat.EmailAddress;

        if (IsInputValidPhoneNumber(input))
            return CloudLoginInputFormat.PhoneNumber;

        return CloudLoginInputFormat.Other;
    }

    public static bool IsInputValidEmailAddress(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        // Normalize email to lowercase for case-insensitive validation
        input = input.Trim().ToLowerInvariant();

        // Improved regex that rejects consecutive dots and other invalid patterns
        return Regex.IsMatch(input, @"^[a-zA-Z0-9]([a-zA-Z0-9._-]*[a-zA-Z0-9])?@[a-zA-Z0-9]([a-zA-Z0-9.-]*[a-zA-Z0-9])?\.[a-zA-Z]{2,}$", RegexOptions.IgnoreCase);
    }

    public bool IsInputValidPhoneNumber(string input) => _cloudGeography.PhoneNumbers.IsValidPhoneNumber(input);

    public async Task<CloudUser?> CurrentUser()
    {
        if (_request.HttpContext.User.Identity?.IsAuthenticated != true)
            return null;

        ClaimsIdentity? userIdentity = _request.HttpContext.User.Identities
            .FirstOrDefault(identity => identity.IsAuthenticated);

        Guid userId;
        string? idClaim = userIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(idClaim, out userId))
        {
            // Read-only compatibility for pre-upgrade cookies. New tickets never contain
            // UserData; the legacy payload is used only to obtain the local id.
            string? legacyPayload = userIdentity?.FindFirst(ClaimTypes.UserData)?.Value;
            CloudUser? legacyUser = string.IsNullOrWhiteSpace(legacyPayload)
                ? null
                : JsonSerializer.Deserialize<CloudUser>(legacyPayload, CloudLoginSerialization.Options);

            if (legacyUser is null || legacyUser.ID == Guid.Empty)
                return null;

            userId = legacyUser.ID;
        }

        if (_cosmosMethods is null)
            return null;

        CloudUser? user = await _cosmosMethods.GetUserById(userId);
        if (user is null || user.IsLocked)
            return null;

        string? currentStamp = await _cosmosMethods.GetSecurityStamp(userId);
        if (!string.IsNullOrWhiteSpace(currentStamp))
        {
            string? ticketStamp = userIdentity?.FindFirst(CloudLoginAuthenticationClaims.SecurityStamp)?.Value;
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(currentStamp),
                    Encoding.UTF8.GetBytes(ticketStamp ?? string.Empty)))
                return null;
        }

        // A ticket names the browser session it belongs to. Signing that device out - from
        // another device, or with "sign out other devices" - revokes the session, and a revoked
        // session is no longer a signed-in user, whatever the cookie says.
        if (_sessionService is not null
            && userIdentity?.FindFirst(CloudLoginAuthenticationClaims.SessionFamily)?.Value is { Length: > 0 } familyId
            && !await _sessionService.IsFamilyActiveAsync(familyId))
            return null;

        if (user != null)
        {
            // normalize blob-stored filenames to public URLs when Azure Storage is configured
            string? baseUrl = _configuration.AzureStorage?.PublicBaseUrl;

            if (!string.IsNullOrWhiteSpace(user.ProfilePicture) && !user.ProfilePicture.Contains("://") && !string.IsNullOrWhiteSpace(baseUrl))
                user.ProfilePicture = baseUrl!.TrimEnd('/') + "/" + user.ProfilePicture.TrimStart('/');

            if (!string.IsNullOrWhiteSpace(user.ProviderProfilePicture) && !user.ProviderProfilePicture.Contains("://") && !string.IsNullOrWhiteSpace(baseUrl))
                user.ProviderProfilePicture = baseUrl!.TrimEnd('/') + "/" + user.ProviderProfilePicture.TrimStart('/');
        }

        return user;
    }

    public async Task<bool> IsAuthenticated()
    {
        CloudUser? user = await CurrentUser();

        return user != null;
    }

    public async Task<List<CloudUser>> GetAllUsers()
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUsers() ?? [];
    }

    public async Task<List<CloudUser>> GetTestUsers()
    {
        if (!IsTestModeEnabled())
            return [];

        List<CloudUser> all = await GetAllUsers();
        return [.. all.Where(u => u.IsTest)];
    }

    private bool IsTestModeEnabled() => _configuration.Providers
        .OfType<LoginTestProviders.TestModeConfiguration>()
        .Any(provider => provider.IsEnabled);

    public async Task<CloudUser?> GetUserById(Guid userId)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserById(userId);
    }

    public async Task<List<CloudUser>> GetUsersByDisplayName(string displayName)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUsersByDisplayName(displayName);
    }

    public async Task<CloudUser?> GetUserByDisplayName(string displayName)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserByDisplayName(displayName);
    }

    public async Task<CloudUser?> GetUserByInput(string input)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserByInput(input);
    }

    public async Task<CloudUser?> GetUserByEmailAddress(string email)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        // Normalize email input
        email = email?.Trim().ToLowerInvariant() ?? string.Empty;

        return await _cosmosMethods.GetUserByEmailAddress(email);
    }

    public async Task<CloudUser?> GetUserByPhoneNumber(string number)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserByPhoneNumber(number);
    }

    public async Task<CloudUser?> GetUserByRequestId(Guid requestId)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserByRequestId(requestId);
    }

    /// <summary>
    /// The browser that completed the interactive sign-in behind a login request. Read before
    /// consuming the request, so a relying party redeeming it over a back channel can attribute
    /// the resulting session to the person's own device rather than to its own server.
    /// Null when the store keeps no such record.
    /// </summary>
    public async Task<CloudLoginRequestOrigin?> GetLoginRequestOrigin(Guid requestId) =>
        _cosmosMethods is null ? null : await _cosmosMethods.GetRequestOrigin(requestId);

    public async Task<Guid> CreateLoginRequest(Guid userId, Guid? requestId = null)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        CloudRequest request = await _cosmosMethods.CreateRequest(userId, requestId);

        return request.GetId();
    }

    public async Task SendWhatsAppCode(string receiver, string code)
    {
        LoginProviders.WhatsAppProviderConfiguration? whatsAppProvider = _configuration.Providers.OfType<LoginProviders.WhatsAppProviderConfiguration>().FirstOrDefault() ?? throw new InvalidOperationException("WhatsApp provider is not configured");

        // Use proper JSON serialization instead of string concatenation
        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = receiver.Replace("+", ""),
            type = "template",
            template = new
            {
                name = whatsAppProvider.Template,
                language = new { code = whatsAppProvider.Language },
                components = new[]
                {
                    new
                    {
                        type = "body",
                        parameters = new[] { new { type = "text", text = code } }
                    }
                }
            }
        };

        string jsonContent = JsonSerializer.Serialize(payload, CloudLoginSerialization.Options);

        using HttpRequestMessage request = new()
        {
            Method = HttpMethod.Post,
            RequestUri = new(whatsAppProvider.RequestUri),
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("Authorization", whatsAppProvider.Authorization);

        // Use IHttpClientFactory if available, otherwise create a new HttpClient
        HttpClient httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();

        try
        {
            HttpResponseMessage response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Failed to send WhatsApp code. Status: {response.StatusCode}, Content: {errorContent}");
            }
        }
        finally
        {
            // Only dispose if we created the HttpClient ourselves
            if (_httpClientFactory == null)
                httpClient.Dispose();
        }
    }

    public async Task SendEmailCode(string receiver, string code)
    {
        if (_configuration.EmailSendCodeRequest == null && _configuration.EmailConfiguration == null)
            throw new InvalidOperationException("Email is not configured.");

        if (_configuration.EmailSendCodeRequest != null)
            await _configuration.EmailSendCodeRequest.Invoke(new CloudLoginSendCodeValue(code, receiver));

        if (_configuration.EmailConfiguration != null)
        {
            string subject = _configuration.EmailConfiguration.DefaultSubject;
            string body = _configuration.EmailConfiguration.DefaultBody.Replace(CloudLoginEmailConfiguration.VerificationCodePlaceHolder, code);
            await _configuration.EmailConfiguration.EmailService.SendEmail(subject, body, [receiver]);
        }
    }

    public async Task UpdateUser(CloudUser user)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        await _cosmosMethods.Update(user);
        
        if (_eventPublisher != null)
            await _eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "User.Updated",
                "User",
                user.ID,
                "Updated",
                new { user.ID }));
    }

    public async Task CreateUser(CloudUser user)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        await _cosmosMethods.Create(user);
        
        if (_eventPublisher != null)
            await _eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "User.Created",
                "User",
                user.ID,
                "Created",
                new { user.ID }));
    }

    public async Task DeleteUser(Guid userId)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        await _cosmosMethods.DeleteUser(userId);

        // Security state (login history, the TOTP secret, passkey public keys) lives outside
        // the user document in blob storage and would otherwise survive account deletion
        // indefinitely. Best-effort: a storage hiccup here must not turn a successful account
        // deletion into a failed one.
        if (_configuration.AzureStorage is not null)
        {
            try
            {
                await SecurityStore.DeleteLoginHistory(userId);
                await SecurityStore.DeleteCredentials(userId);
            }
            catch
            {
                // Intentionally ignored — see summary.
            }
        }

        if (_eventPublisher != null)
            await _eventPublisher.PublishAsync(CloudLoginEvent.Create(
                "User.Deleted",
                "User",
                userId,
                "Deleted",
                new { ID = userId }));
    }

    public async Task AddUserInput(Guid userId, CloudLoginInput input)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        await _cosmosMethods.AddInput(userId, input);
    }

    public async Task<string> UploadProfilePicture(Guid userId, byte[] content, string contentType)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        if (_configuration.AzureStorage is null)
            throw new InvalidOperationException("Azure Storage is not configured.");

        if (content == null || content.Length == 0)
            throw new ArgumentException("Image content is empty.", nameof(content));

        if (content.Length > _configuration.Security.MaximumProfileImageBytes)
            throw new ArgumentException("Image content exceeds the configured size limit.", nameof(content));

        contentType = contentType.Trim().ToLowerInvariant();
        if (!HasValidImageSignature(content, contentType))
            throw new ArgumentException("Unsupported image type or invalid image content.", nameof(contentType));

        CloudUser user = await _cosmosMethods.GetUserById(userId)
            ?? throw new Exception($"User {userId} not found.");

        string ext = contentType switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/jpg" or "image/jpeg" => ".jpg",
            _ => throw new ArgumentException("Unsupported image type.", nameof(contentType))
        };

        string fileName = $"{Guid.NewGuid():N}{ext}";

        Azure.Storage.Blobs.BlobContainerClient container = _configuration.AzureStorage.CreateContainerClient();
        await container.CreateIfNotExistsAsync();

        Azure.Storage.Blobs.BlobClient blob = container.GetBlobClient(fileName);
        Azure.Storage.Blobs.Models.BlobHttpHeaders headers = new() { ContentType = contentType };

        using MemoryStream stream = new(content);
        await blob.UploadAsync(stream, headers);

        // Preserve the current provider picture (if not already a custom one) so it can be restored later.
        if (!user.IsCustomProfilePicture && !string.IsNullOrWhiteSpace(user.ProfilePicture))
            user.ProviderProfilePicture = user.ProfilePicture;

        user.ProfilePicture = fileName;
        user.IsCustomProfilePicture = true;
        await _cosmosMethods.Update(user);

        string? baseUrl = _configuration.AzureStorage.PublicBaseUrl;

        return !string.IsNullOrWhiteSpace(baseUrl)
            ? baseUrl!.TrimEnd('/') + "/" + fileName
            : fileName;
    }

    internal static bool HasValidImageSignature(byte[] content, string contentType) => contentType switch
    {
        "image/png" => content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/jpg" or "image/jpeg" => content.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        "image/gif" => content.AsSpan().StartsWith("GIF87a"u8) || content.AsSpan().StartsWith("GIF89a"u8),
        "image/webp" => content.Length >= 12 &&
                         content.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                         content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };

    // ── Admin methods ──────────────────────────────────────────────────

    public async Task SetUserLocked(Guid userId, bool locked)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        CloudUser user = await _cosmosMethods.GetUserById(userId)
            ?? throw new Exception($"User {userId} not found.");

        user.IsLocked = locked;
        await _cosmosMethods.Update(user);
        await _cosmosMethods.RotateSecurityStamp(userId);
    }

    public async Task AdminResetPassword(Guid userId, string newPassword)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        if (!IsValidPassword(newPassword))
            throw new ArgumentException("Password does not meet requirements.", nameof(newPassword));

        CloudUser user = await _cosmosMethods.GetUserById(userId) ?? throw new Exception($"User {userId} not found.");

        string hashed = await HashPassword(newPassword);

        foreach (CloudLoginInput input in user.Inputs)
        {
            CloudLoginProvider? provider = input.Providers.FirstOrDefault(p => p.Code.Equals("Password", StringComparison.OrdinalIgnoreCase));

            if (provider != null)
                provider.PasswordHash = hashed;
        }

        await _cosmosMethods.Update(user);
        await _cosmosMethods.RotateSecurityStamp(userId);
    }

    public async Task SetGlobalAdmin(Guid userId, bool isAdmin)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        CloudUser user = await _cosmosMethods.GetUserById(userId) ?? throw new Exception($"User {userId} not found.");

        user.IsGlobalAdmin = isAdmin;
        await _cosmosMethods.Update(user);
        await _cosmosMethods.RotateSecurityStamp(userId);
    }

    public async Task<int> GetUserCount()
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserCount();
    }

    public async Task<bool> AutomaticLogin()
    {
        throw new NotImplementedException("AutomaticLogin feature is not yet implemented");
    }

    public async Task<List<CloudLoginProviderDefinition>> GetProviders()
    {
        if (_configuration.Providers == null)
            throw new InvalidOperationException("Providers configuration is not initialized");

        List<CloudLoginProviderDefinition> providers = [.. _configuration.Providers
            .Where(provider => provider is not LoginTestProviders.TestModeConfiguration testMode || testMode.IsEnabled)
            .Select(provider => provider.ToModel())];

        return providers;
    }

    public string GetPhoneNumber(string input) => _cloudGeography.PhoneNumbers.Get(input).Number;

    // Model-based authentication methods
    public async Task<bool> PasswordLogin(CloudLoginPasswordLoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return false;

        CloudUser? user = await ValidateEmailPassword(request.Email, request.Password);
        if (user == null)
            return false;

        await SignInUserAsync(user, request.KeepMeSignedIn, "Password");
        return true;
    }

    public async Task<bool> TestLogin(Guid userId, bool keepMeSignedIn = false)
    {
        if (userId == Guid.Empty || !IsTestModeEnabled())
            return false;

        CloudUser? user = await GetUserById(userId);
        if (user?.IsTest != true || user.IsLocked)
            return false;

        user.LastSignedIn = DateTimeOffset.UtcNow;
        await UpdateUser(user);
        await SignInUserAsync(user, keepMeSignedIn, "TestMode");
        return true;
    }

    private async Task SignInUserAsync(CloudUser user, bool keepMeSignedIn, string authenticationType)
    {
        if (user.IsLocked)
            throw new UnauthorizedAccessException("The account is locked.");

        // The single choke point for every sign-in that does not go through a provider challenge
        // (password, verification code, test mode), so a restricted sign-in profile applies here
        // exactly as it does to a provider redirect.
        if (!SignInProfileAllows(authenticationType))
            throw new UnauthorizedAccessException("This sign-in method is not allowed for the requested sign-in profile.");

        ClaimsPrincipal principal = await CloudLoginAuthenticationClaims.CreateAsync(
            user, authenticationType, _cosmosMethods);
        AuthenticationProperties properties = new()
        {
            IsPersistent = keepMeSignedIn,
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(_configuration.LoginDuration) : null
        };

        await _accessor.HttpContext!.SignInAsync(principal, properties);

        // A provider sign-in is recorded by the cookie handler as it converts the provider's
        // principal; a local one arrives already converted and skips that, so it is recorded here
        // - otherwise "Recent sign-ins" stayed empty for everyone who signs in with a password,
        // a code, or a test account.
        await RecordSignInAsync(user, authenticationType);
    }

    /// <summary>
    /// Appends the sign-in that just completed on this request to the user's security timeline.
    /// Best-effort: the history must never be what fails a sign-in.
    /// </summary>
    private async Task RecordSignInAsync(CloudUser user, string provider)
    {
        Microsoft.AspNetCore.Http.HttpContext? context = _accessor.HttpContext;

        if (context is null)
            return;

        string? userAgent = context.Request.Headers.UserAgent.ToString();

        await RecordSignInForUser(user.ID, new CloudLoginHistoryEntry
        {
            SignedInOn = DateTimeOffset.UtcNow,
            Provider = provider,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            Device = string.IsNullOrWhiteSpace(userAgent) ? null : Core.Application.DeviceDescription.Parse(userAgent).Name
        });
    }

    /// <summary>
    /// Ends the browser session behind the caller's cookie - its own family and every application
    /// family signed in from it - so the device drops off the account page's list and any tokens
    /// minted from this sign-in stop refreshing. Best-effort: signing out must always succeed.
    /// </summary>
    private async Task RevokeOwnSessionAsync()
    {
        if (_sessionService is null)
            return;

        (string? sessionId, string? familyId) = CloudLoginAuthenticationClaims.SessionOf(_accessor.HttpContext?.User);

        try
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
                await _sessionService.RevokeSessionAsync(sessionId, Core.Domain.SessionRevocationReasons.UserSignedOut);
            else if (!string.IsNullOrWhiteSpace(familyId))
                await _sessionService.RevokeFamilyAsync(familyId, Core.Domain.SessionRevocationReasons.UserSignedOut);
        }
        catch
        {
            // Intentionally ignored - see summary.
        }
    }

    public async Task<CloudUser> PasswordRegistration(CloudLoginPasswordRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FirstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        LoginTestProviders.TestModeConfiguration? testProvider = _configuration.Providers
            .OfType<LoginTestProviders.TestModeConfiguration>()
            .FirstOrDefault();
        bool isTestModeRegistration = testProvider?.IsEnabled == true && string.IsNullOrWhiteSpace(request.Password);

        if (!isTestModeRegistration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

            if (!IsValidPassword(request.Password!))
                throw new ArgumentException(
                    $"Password must be between {_configuration.Security.MinimumPasswordLength} and {_configuration.Security.MaximumPasswordLength} characters and must not be blocked.",
                    nameof(request.Password));
        }

        // Ensure user doesn't already exist
        CloudUser? existing = request.InputFormat switch
        {
            CloudLoginInputFormat.EmailAddress => await GetUserByEmailAddress(request.Input),
            CloudLoginInputFormat.PhoneNumber => await GetUserByPhoneNumber(request.Input),
            _ => throw new ArgumentException("Invalid input format for registration", nameof(request.InputFormat))
        };

        if (existing != null)
            throw new Exception("User already exists.");

        CloudUser newUser = new()
        {
            ID = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName,
            IsTest = isTestModeRegistration,
            CreatedOn = DateTimeOffset.UtcNow,
            LastSignedIn = DateTimeOffset.UtcNow,
            Inputs = [new() {
                Input = request.InputFormat == CloudLoginInputFormat.EmailAddress ? request.Input.Trim().ToLowerInvariant() : request.Input,
                Format = request.InputFormat,
                IsPrimary = true,
                Providers = isTestModeRegistration ? [] :
                [
                    new()
                    {
                        Code = "Code",
                        Identifier = null
                    },
                    new()
                    {
                        Code = "Password",
                        PasswordHash = await HashPassword(request.Password!),
                        Identifier = null
                    }
                ]
            }]
        };

        await CreateUser(newUser);

        return newUser;
    }

    public async Task<CloudUser> CodeRegistration(CloudLoginCodeRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FirstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        // Ensure user doesn't already exist
        CloudUser? existing = request.InputFormat switch
        {
            CloudLoginInputFormat.EmailAddress => await GetUserByEmailAddress(request.Input),
            CloudLoginInputFormat.PhoneNumber => await GetUserByPhoneNumber(request.Input),
            _ => throw new ArgumentException("Invalid input format for registration", nameof(request.InputFormat))
        };

        if (existing != null)
            throw new Exception("User already exists.");

        CloudUser newUser = new()
        {
            ID = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName,
            CreatedOn = DateTimeOffset.UtcNow,
            LastSignedIn = DateTimeOffset.UtcNow,
            Inputs = [new() {
                Input = request.InputFormat == CloudLoginInputFormat.EmailAddress ? request.Input.Trim().ToLowerInvariant() : request.Input,
                Format = request.InputFormat,
                IsPrimary = true,
                Providers =
                [
                    new()
                    {
                        Code = "Code",
                        Identifier = null // Internal providers don't have external identifiers
                    }
                ]
            }]
        };

        await CreateUser(newUser);

        return newUser;
    }

    public Task<string> HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hashed = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount: _configuration.Security.PasswordHashIterations,
            numBytesRequested: 32);

        string encoded = $"pbkdf2-sha256${_configuration.Security.PasswordHashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hashed)}";
        return Task.FromResult(encoded);
    }

    public bool IsValidPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        if (password.Length < _configuration.Security.MinimumPasswordLength ||
            password.Length > _configuration.Security.MaximumPasswordLength)
            return false;

        if (password.Any(char.IsControl))
            return false;

        return !_configuration.Security.PasswordBlocklist.Contains(password);
    }

}
