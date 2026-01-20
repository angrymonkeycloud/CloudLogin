// CloudLoginServer/Services/CloudLoginAuthenticationService.cs
using AngryMonkey.Cloud;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Server;

public class CloudLoginAuthenticationService(IServiceProvider serviceProvider)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public async Task HandleSignIn(ClaimsPrincipal principal, HttpContext context)
    {
        if (principal.FindFirst(ClaimTypes.Hash)?.Value?.Equals("CloudLogin") ?? false)
            return;

        CosmosMethods? cosmosMethods = context.RequestServices.GetService<CosmosMethods>();
        if (cosmosMethods == null)
            return;

        DateTimeOffset currentDateTime = DateTimeOffset.UtcNow;
        await ProcessUserSignIn(principal, cosmosMethods, currentDateTime);
    }

    private async Task ProcessUserSignIn(ClaimsPrincipal principal, CosmosMethods cosmosMethods, DateTimeOffset currentDateTime)
    {
        InputFormat formatValue = principal.HasClaim(claim => claim.Type == ClaimTypes.Email)
        ? InputFormat.EmailAddress
        : InputFormat.PhoneNumber;

        string input = GetUserInput(principal, formatValue);
        string providerName = GetProviderName(principal);
        string? providerIdentifier = GetProviderIdentifier(principal);

        User? user = await GetExistingUser(cosmosMethods, input, formatValue);

        if (user != null)
        {
            await UpdateExistingUser(user, principal, providerName, providerIdentifier, input, formatValue, currentDateTime, cosmosMethods);
        }
        else
        {
            await CreateNewUser(principal, providerName, providerIdentifier, input, formatValue, currentDateTime, cosmosMethods);
        }
    }

    private static string GetUserInput(ClaimsPrincipal principal, InputFormat format)
    {
        string input = format == InputFormat.EmailAddress
        ? principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty
        : principal.FindFirst(ClaimTypes.MobilePhone)?.Value ?? string.Empty;

        // Normalize email addresses
        if (format == InputFormat.EmailAddress)
        {
            input = input.Trim().ToLowerInvariant();
        }

        return input;
    }

    private static string GetProviderName(ClaimsPrincipal principal)
    {
        ClaimsIdentity? identity = principal.Identity as ClaimsIdentity;
        return identity?.AuthenticationType ?? "External";
    }

    private static string? GetProviderIdentifier(ClaimsPrincipal principal)
    {
        // Try to get the provider-specific user identifier
        // Different providers use different claim types for user identifiers
        string? nameIdentifier = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(nameIdentifier))
            return nameIdentifier;

        // Fallback to other common identifier claims
        string? subject = principal.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(subject))
            return subject;

        string? oid = principal.FindFirst("oid")?.Value;
        if (!string.IsNullOrEmpty(oid))
            return oid;

        string? id = principal.FindFirst("id")?.Value;
        if (!string.IsNullOrEmpty(id))
            return id;

        return null;
    }

    private static async Task<User?> GetExistingUser(CosmosMethods cosmosMethods, string input, InputFormat format)
    {
        return format == InputFormat.EmailAddress
        ? await cosmosMethods.GetUserByEmailAddress(input)
        : await cosmosMethods.GetUserByPhoneNumber(input);
    }

    private async Task UpdateExistingUser(User user, ClaimsPrincipal principal, string providerName, string? providerIdentifier, string input, InputFormat formatValue, DateTimeOffset currentDateTime, CosmosMethods cosmosMethods)
    {
        // Update user information with latest from provider, but never override existing non-empty values
        user.FirstName = string.IsNullOrWhiteSpace(user.FirstName) ? (principal.FindFirst(ClaimTypes.GivenName)?.Value ?? user.FirstName) : user.FirstName;
        user.LastName = string.IsNullOrWhiteSpace(user.LastName) ? (principal.FindFirst(ClaimTypes.Surname)?.Value ?? user.LastName) : user.LastName;
        user.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? (principal.FindFirst(ClaimTypes.Name)?.Value ?? $"{user.FirstName} {user.LastName}") : user.DisplayName;

        // Profile picture: only set when missing
        if (string.IsNullOrWhiteSpace(user.ProfilePicture))
        {
            string? providerPictureUrl = GetProfilePictureUrl(principal);
            if (!string.IsNullOrWhiteSpace(providerPictureUrl))
                user.ProfilePicture = await IngestProfilePicture(providerPictureUrl);
        }

        string? country = NormalizeCountry(GetCountry(principal));

        if (string.IsNullOrWhiteSpace(user.Country) && !string.IsNullOrWhiteSpace(country))
            user.Country = country;

        string? locale = NormalizeLocale(GetLocale(principal));

        if (string.IsNullOrWhiteSpace(user.Locale) && !string.IsNullOrWhiteSpace(locale))
            user.Locale = locale;

        // Date of birth (only if not already set)
        if (user.DateOfBirth is null)
        {
            DateOnly? dob = GetDateOfBirth(principal);

            if (dob is not null)
                user.DateOfBirth = dob;
        }

        // Normalize input for comparison
        string normalizedInput = formatValue == InputFormat.EmailAddress
        ? input.Trim().ToLowerInvariant()
        : input;

        // Find the existing input that matches
        LoginInput? existingInput = user.Inputs.FirstOrDefault(i =>
        string.Equals(i.Input, normalizedInput, StringComparison.OrdinalIgnoreCase));

        if (existingInput != null)
        {
            // Find existing provider or add new one
            LoginProvider? existingProvider = existingInput.Providers.FirstOrDefault(p =>
            string.Equals(p.Code, providerName, StringComparison.OrdinalIgnoreCase));

            if (existingProvider != null)
            {
                // Update provider identifier if it's missing
                if (string.IsNullOrEmpty(existingProvider.Identifier) && !string.IsNullOrEmpty(providerIdentifier))
                {
                    existingProvider.Identifier = providerIdentifier;
                }
            }
            else
            {
                // Add new provider with identifier
                existingInput.Providers.Add(new LoginProvider
                {
                    Code = providerName,
                    Identifier = providerIdentifier
                });
            }
        }
        else
        {
            // This shouldn't happen if the user was found by this input, but add it as fallback
            user.Inputs.Add(new LoginInput
            {
                Input = normalizedInput,
                Format = formatValue,
                IsPrimary = user.Inputs.Count == 0,
                Providers = [new LoginProvider
 {
 Code = providerName,
 Identifier = providerIdentifier
 }]
            });
        }

        user.LastSignedIn = currentDateTime;
        await cosmosMethods.Update(user);
    }

    private async Task CreateNewUser(ClaimsPrincipal principal, string providerName, string? providerIdentifier, string input, InputFormat formatValue, DateTimeOffset currentDateTime, CosmosMethods cosmosMethods)
    {
        (string? countryCode, string? callingCode, string formattedInput) = await ProcessPhoneNumber(formatValue, input);

        // Ensure email is normalized
        if (formatValue == InputFormat.EmailAddress)
        {
            formattedInput = formattedInput.Trim().ToLowerInvariant();
        }

        string firstName = principal.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
        string lastName = principal.FindFirst(ClaimTypes.Surname)?.Value ?? "";
        string displayName = principal.FindFirst(ClaimTypes.Name)?.Value ?? $"{firstName} {lastName}";

        string? providerPictureUrl = GetProfilePictureUrl(principal);
        string? storedPicture = null;

        if (!string.IsNullOrWhiteSpace(providerPictureUrl))
            storedPicture = await IngestProfilePicture(providerPictureUrl);

        // Optional profile info
        string? country = NormalizeCountry(GetCountry(principal));
        string? locale = NormalizeLocale(GetLocale(principal));
        DateOnly? dob = GetDateOfBirth(principal);

        User user = new()
        {
            ID = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName.Trim(),
            CreatedOn = currentDateTime,
            LastSignedIn = currentDateTime,
            ProfilePicture = storedPicture,
            Country = country,
            Locale = locale,
            DateOfBirth = dob,
            Inputs =
        [
        new LoginInput()
 {
 Input = formattedInput,
 Format = formatValue,
 IsPrimary = true,
 PhoneNumberCountryCode = countryCode,
 PhoneNumberCallingCode = callingCode,
 Providers = [new LoginProvider
 {
 Code = providerName,
 Identifier = providerIdentifier
 }]
 }
        ]
        };

        await cosmosMethods.Create(user);
    }

    private async Task<(string? countryCode, string? callingCode, string input)> ProcessPhoneNumber(InputFormat formatValue, string input)
    {
        if (formatValue != InputFormat.PhoneNumber)
            return (null, null, input);

        CloudGeographyClient? cloudGeography = _serviceProvider.GetService<CloudGeographyClient>();

        if (cloudGeography == null || string.IsNullOrEmpty(input))
            return (null, null, input);

        Cloud.Geography.PhoneNumber phoneNumber = cloudGeography.PhoneNumbers.Get(input);

        return (phoneNumber.CountryCode, phoneNumber.CountryCallingCode, phoneNumber.Number);
    }

    private static string? GetProfilePictureUrl(ClaimsPrincipal principal)
    {
        // Common claim names across providers
        string? picture = principal.FindFirst("picture")?.Value
        ?? principal.FindFirst("urn:google:picture")?.Value
        ?? principal.FindFirst("avatar_url")?.Value
        ?? principal.FindFirst("picture_url")?.Value
        ?? principal.FindFirst("profile")?.Value
        ?? principal.FindFirst(ClaimTypes.Uri)?.Value;

        // Basic sanity check
        if (!string.IsNullOrWhiteSpace(picture) && Uri.IsWellFormedUriString(picture, UriKind.Absolute))
            return picture;

        return null;
    }

    private static string? GetCountry(ClaimsPrincipal principal)
    {
        string? country = principal.FindFirst(ClaimTypes.Country)?.Value
        ?? principal.FindFirst("country")?.Value
        ?? TryGetCountryFromAddress(principal);

        return country;
    }

    private static string? TryGetCountryFromAddress(ClaimsPrincipal principal)
    {
        string? addressJson = principal.FindFirst("address")?.Value;
        if (string.IsNullOrWhiteSpace(addressJson))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(addressJson);
            if (doc.RootElement.TryGetProperty("country", out JsonElement countryEl))
                return countryEl.GetString();
        }
        catch { }
        return null;
    }

    private static string? GetLocale(ClaimsPrincipal principal)
    {
        return principal.FindFirst("locale")?.Value
        ?? principal.FindFirst("urn:google:locale")?.Value
        ?? principal.FindFirst("ui_locale")?.Value // Facebook
        ?? principal.FindFirst("lang")?.Value
        ?? principal.FindFirst("language")?.Value;
    }

    private static string? NormalizeCountry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        // If already a2-letter code, force uppercase
        if (value.Length == 2) return value.ToUpperInvariant();

        // Otherwise, try to map via RegionInfo if possible
        try
        {
            RegionInfo region = new(value);
            return region.TwoLetterISORegionName.ToUpperInvariant();
        }
        catch { return value.ToUpperInvariant(); }
    }

    private static string? NormalizeLocale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim().Replace('_', '-');

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(value);
            return culture.Name; // e.g., en-US
        }
        catch { return value; }
    }

    private static DateOnly? GetDateOfBirth(ClaimsPrincipal principal)
    {
        string? dobStr = principal.FindFirst(ClaimTypes.DateOfBirth)?.Value
        ?? principal.FindFirst("birthdate")?.Value
        ?? principal.FindFirst("birthday")?.Value
        ?? principal.FindFirst("bdate")?.Value;

        if (string.IsNullOrWhiteSpace(dobStr))
            return null;

        // Try multiple formats
        string[] formats =
        [
        "yyyy-MM-dd",
 "MM/dd/yyyy",
 "dd/MM/yyyy",
 "M/d/yyyy",
 "d/M/yyyy",
 "yyyyMMdd",
 "dd-MM-yyyy",
 "d-M-yyyy",
 "M-d-yyyy",
 "yyyy/MM/dd"
        ];

        foreach (string f in formats)
            if (DateTime.TryParseExact(dobStr, f, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime dt))
                return DateOnly.FromDateTime(dt);

        // Fallback to flexible parse
        if (DateTime.TryParse(dobStr, out DateTime any))
            return DateOnly.FromDateTime(any);

        return null;
    }

    private async Task<string?> IngestProfilePicture(string providerPictureUrl)
    {
        // Decide according to configuration: upload to Azure Storage or return URL
        CloudLoginWebConfiguration? config = _serviceProvider.GetService<CloudLoginWebConfiguration>();

        if (config?.AzureStorage is null)
            return providerPictureUrl;

        try
        {
            // Download the image
            HttpClient httpClient = _serviceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
            using HttpResponseMessage resp = await httpClient.GetAsync(providerPictureUrl);
            resp.EnsureSuccessStatusCode();
            await using Stream stream = await resp.Content.ReadAsStreamAsync();
            string? contentType = resp.Content.Headers.ContentType?.MediaType;
            string ext = GuessExtension(providerPictureUrl, contentType);
            string fileName = $"{Guid.NewGuid():N}{ext}";

            // Upload to blob storage
            BlobContainerClient container = new(config.AzureStorage.ConnectionString!, config.AzureStorage.ContainerName!);
            await container.CreateIfNotExistsAsync();
            BlobClient blob = container.GetBlobClient(fileName);
            BlobHttpHeaders headers = new() { ContentType = contentType ?? "image/jpeg" };
            await blob.UploadAsync(stream, headers);

            // Persist only the file name
            return fileName;
        }
        catch
        {
            // On any failure, fall back to original URL to avoid blocking sign-in
            return providerPictureUrl;
        }
    }

    private static string GuessExtension(string url, string? contentType)
    {
        // Prefer content-type
        return contentType switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/svg+xml" => ".svg",
            "image/jpg" or "image/jpeg" => ".jpg",
            _ =>
 Path.GetExtension(new Uri(url).LocalPath) switch
 {
     ".png" or ".gif" or ".webp" or ".bmp" or ".svg" or ".jpg" or ".jpeg" => Path.GetExtension(new Uri(url).LocalPath).ToLowerInvariant(),
     _ => ".jpg"
 }
        };
    }
}
