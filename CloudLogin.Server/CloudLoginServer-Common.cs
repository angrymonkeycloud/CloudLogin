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
            parameters.Add($"referer={referer}");

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
            parameters.Add($"referer={referer}");

        if (isMobileApp)
            parameters.Add("isMobileApp=true");

        if (keepMeSignedIn)
            parameters.Add("keepMeSignedIn=true");

        if (!string.IsNullOrEmpty(userHint))
            parameters.Add($"input={HttpUtility.UrlEncode(userHint)}");

        string queryString = parameters.Count > 0 ? "?" + string.Join("&", parameters) : "";
        return $"{baseUrl}/cloudlogin/login{queryString}";
    }

    public InputFormat GetInputFormat(string input)
    {
        if (string.IsNullOrEmpty(input))
            return InputFormat.Other;

        if (IsInputValidEmailAddress(input))
            return InputFormat.EmailAddress;

        if (IsInputValidPhoneNumber(input))
            return InputFormat.PhoneNumber;

        return InputFormat.Other;
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

    public async Task<User?> CurrentUser()
    {
        string? userCookie = _request.Cookies["CloudLogin"];

        if (userCookie == null)
            return null;

        ClaimsIdentity userIdentity = _request.HttpContext.User.Identities.First();

        string? loginIdentity = userIdentity.FindFirst(ClaimTypes.UserData)?.Value;

        if (string.IsNullOrEmpty(loginIdentity))
            return null;

        User? user = JsonSerializer.Deserialize<User?>(loginIdentity, CloudLoginSerialization.Options);

        if (user != null)
        {
            // normalize blob-stored filenames to public URLs when Azure Storage is configured
            string? baseUrl = _configuration.AzureStorage?.PublicBaseUrl;
            if (!string.IsNullOrWhiteSpace(user.ProfilePicture) && !user.ProfilePicture.Contains("://") && !string.IsNullOrWhiteSpace(baseUrl))
                user.ProfilePicture = baseUrl!.TrimEnd('/') + "/" + user.ProfilePicture.TrimStart('/');
        }

        return user;
    }

    public async Task<bool> IsAuthenticated()
    {
        return _request.Cookies["CloudLogin"] != null;
    }

    public async Task<List<User>> GetAllUsers()
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUsers() ?? [];
    }

    public async Task<User?> GetUserById(Guid userId)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserById(userId);
    }

    public async Task<List<User>> GetUsersByDisplayName(string displayName)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUsersByDisplayName(displayName);
    }

    public async Task<User?> GetUserByDisplayName(string displayName)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserByDisplayName(displayName);
    }

    public async Task<User?> GetUserByInput(string input)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserByInput(input);
    }

    public async Task<User?> GetUserByEmailAddress(string email)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        // Normalize email input
        email = email?.Trim().ToLowerInvariant() ?? string.Empty;

        return await _cosmosMethods.GetUserByEmailAddress(email);
    }

    public async Task<User?> GetUserByPhoneNumber(string number)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserByPhoneNumber(number);
    }

    public async Task<User?> GetUserByRequestId(Guid requestId)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        return await _cosmosMethods.GetUserByRequestId(requestId);
    }

    public async Task<Guid> CreateLoginRequest(Guid userId, Guid? requestId = null)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        LoginRequest request = await _cosmosMethods.CreateRequest(userId, requestId);

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
            await _configuration.EmailSendCodeRequest.Invoke(new SendCodeValue(code, receiver));

        if (_configuration.EmailConfiguration != null)
        {
            string subject = _configuration.EmailConfiguration.DefaultSubject;
            string body = _configuration.EmailConfiguration.DefaultBody.Replace(CloudLoginEmailConfiguration.VerificationCodePlaceHolder, code);
            await _configuration.EmailConfiguration.EmailService.SendEmail(subject, body, [receiver]);
        }
    }

    public async Task UpdateUser(User user)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        await _cosmosMethods.Update(user);
    }

    public async Task CreateUser(User user)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        await _cosmosMethods.Create(user);
    }

    public async Task DeleteUser(Guid userId)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        await _cosmosMethods.DeleteUser(userId);
    }

    public async Task AddUserInput(Guid userId, LoginInput input)
    {
        if (_cosmosMethods == null)
            throw new InvalidOperationException("CosmosMethods is not initialized");

        await _cosmosMethods.AddInput(userId, input);
    }

    public async Task<bool> AutomaticLogin()
    {
        throw new NotImplementedException("AutomaticLogin feature is not yet implemented");
    }

    public async Task<List<ProviderDefinition>> GetProviders()
    {
        if (_configuration.Providers == null)
            throw new InvalidOperationException("Providers configuration is not initialized");

        List<ProviderDefinition> providers = [.. _configuration.Providers.Select(key => key.ToModel())];

        return providers;
    }

    public string GetPhoneNumber(string input) => _cloudGeography.PhoneNumbers.Get(input).Number;

    // Model-based authentication methods
    public async Task<bool> PasswordLogin(PasswordLoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return false;

        User? user = await ValidateEmailPassword(request.Email, request.Password);
        if (user == null)
            return false;

        // Create claims for the authenticated user
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.ID.ToString()),
            new(ClaimTypes.Email, request.Email.ToLowerInvariant()),
            new(ClaimTypes.Name, user.DisplayName ?? $"{user.FirstName} {user.LastName}"),
            new(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
            new(ClaimTypes.Surname, user.LastName ?? string.Empty),
            new(ClaimTypes.UserData, JsonSerializer.Serialize(user, CloudLoginSerialization.Options))
        ];

        ClaimsIdentity identity = new(claims, "Password");
        ClaimsPrincipal principal = new(identity);

        AuthenticationProperties properties = new()
        {
            IsPersistent = request.KeepMeSignedIn,
            ExpiresUtc = request.KeepMeSignedIn ? DateTimeOffset.UtcNow.AddDays(30) : null
        };

        await _accessor.HttpContext!.SignInAsync(principal, properties);
        return true;
    }

    public async Task<User> PasswordRegistration(PasswordRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FirstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        // Ensure user doesn't already exist
        User? existing = request.InputFormat switch
        {
            InputFormat.EmailAddress => await GetUserByEmailAddress(request.Input),
            InputFormat.PhoneNumber => await GetUserByPhoneNumber(request.Input),
            _ => throw new ArgumentException("Invalid input format for registration", nameof(request.InputFormat))
        };

        if (existing != null)
            throw new Exception("User already exists.");

        User newUser = new()
        {
            ID = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName,
            CreatedOn = DateTimeOffset.UtcNow,
            LastSignedIn = DateTimeOffset.UtcNow,
            Inputs = [new() {
                Input = request.InputFormat == InputFormat.EmailAddress ? request.Input.Trim().ToLowerInvariant() : request.Input,
                Format = request.InputFormat,
                IsPrimary = true,
                Providers = 
                [
                    new()
                    {
                        Code = "Code",
                        Identifier = null // Internal providers don't have external identifiers
                    },
                    new()
                    {
                        Code = "Password",
                        PasswordHash = await HashPassword(request.Password),
                        Identifier = null // Internal providers don't have external identifiers
                    }
                ]
            }]
        };

        await CreateUser(newUser);

        return newUser;
    }

    public async Task<User> CodeRegistration(CodeRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FirstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        // Ensure user doesn't already exist
        User? existing = request.InputFormat switch
        {
            InputFormat.EmailAddress => await GetUserByEmailAddress(request.Input),
            InputFormat.PhoneNumber => await GetUserByPhoneNumber(request.Input),
            _ => throw new ArgumentException("Invalid input format for registration", nameof(request.InputFormat))
        };

        if (existing != null)
            throw new Exception("User already exists.");

        User newUser = new()
        {
            ID = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName,
            CreatedOn = DateTimeOffset.UtcNow,
            LastSignedIn = DateTimeOffset.UtcNow,
            Inputs = [new() {
                Input = request.InputFormat == InputFormat.EmailAddress ? request.Input.Trim().ToLowerInvariant() : request.Input,
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

    public async Task<string> HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hashed = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount: 100000,
            numBytesRequested: 32);

        // Return as base64(salt + hash)
        return Convert.ToBase64String(salt.Concat(hashed).ToArray());
    }

    public bool IsValidPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        // Password must be at least 8 characters long
        if (password.Length < 8)
            return false;

        // Password must contain at least one lowercase letter
        if (!password.Any(char.IsLower))
            return false;

        // Password must contain at least one uppercase letter
        if (!password.Any(char.IsUpper))
            return false;

        // Password must contain at least one digit
        if (!password.Any(char.IsDigit))
            return false;

        // Password must contain at least one special character
        string specialChars = "!@#$%^&*()_+-=[]{}|;:,.<>?";

        if (!password.Any(specialChars.Contains))
            return false;

        return true;
    }
}
