// CloudLoginServer/Services/CloudLoginAuthenticationService.cs
using AngryMonkey.Cloud;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

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

        User? user = await GetExistingUser(cosmosMethods, input, formatValue);

        if (user != null)
        {
            await UpdateExistingUser(user, principal, providerName, input, formatValue, currentDateTime, cosmosMethods);
        }
        else
        {
            await CreateNewUser(principal, providerName, input, formatValue, currentDateTime, cosmosMethods);
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
        var identity = principal.Identity as ClaimsIdentity;
        return identity?.AuthenticationType ?? "External";
    }

    private static async Task<User?> GetExistingUser(CosmosMethods cosmosMethods, string input, InputFormat format)
    {
        return format == InputFormat.EmailAddress
            ? await cosmosMethods.GetUserByEmailAddress(input)
            : await cosmosMethods.GetUserByPhoneNumber(input);
    }

    private static async Task UpdateExistingUser(User user, ClaimsPrincipal principal, string providerName, string input, InputFormat formatValue, DateTimeOffset currentDateTime, CosmosMethods cosmosMethods)
    {
        // Update user information with latest from provider
        user.FirstName ??= principal.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty;
        user.LastName ??= principal.FindFirst(ClaimTypes.Surname)?.Value ?? string.Empty;
        user.DisplayName ??= principal.FindFirst(ClaimTypes.Name)?.Value ?? $"{user.FirstName} {user.LastName}";

        // Normalize input for comparison
        string normalizedInput = formatValue == InputFormat.EmailAddress 
            ? input.Trim().ToLowerInvariant() 
            : input;

        // Find the existing input that matches
        LoginInput? existingInput = user.Inputs.FirstOrDefault(i => 
            string.Equals(i.Input, normalizedInput, StringComparison.OrdinalIgnoreCase));

        if (existingInput != null)
        {
            // Add provider if it doesn't exist
            if (!existingInput.Providers.Any(p => string.Equals(p.Code, providerName, StringComparison.OrdinalIgnoreCase)))
            {
                existingInput.Providers.Add(new LoginProvider { Code = providerName });
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
                Providers = [new LoginProvider { Code = providerName }]
            });
        }

        user.LastSignedIn = currentDateTime;
        await cosmosMethods.Update(user);
    }

    private async Task CreateNewUser(ClaimsPrincipal principal, string providerName, string input, InputFormat formatValue, DateTimeOffset currentDateTime, CosmosMethods cosmosMethods)
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

        User user = new()
        {
            ID = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName.Trim(),
            CreatedOn = currentDateTime,
            LastSignedIn = currentDateTime,
            Inputs =
            [
                new LoginInput()
                {
                    Input = formattedInput,
                    Format = formatValue,
                    IsPrimary = true,
                    PhoneNumberCountryCode = countryCode,
                    PhoneNumberCallingCode = callingCode,
                    Providers = [new LoginProvider { Code = providerName }]
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
}
