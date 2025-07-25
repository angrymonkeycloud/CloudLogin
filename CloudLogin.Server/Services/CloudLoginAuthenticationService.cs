﻿// CloudLoginServer/Services/CloudLoginAuthenticationService.cs
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

        User? user = await GetExistingUser(cosmosMethods, input, formatValue);

        if (user != null)
            await UpdateUserLastSignedIn(user.ID, currentDateTime, cosmosMethods);
        else
            throw new UnauthorizedAccessException($"User with {(formatValue == InputFormat.EmailAddress ? "email" : "phone number")} '{input}' does not exist.");
    }

    private static string GetUserInput(ClaimsPrincipal principal, InputFormat format)
    {
        return format == InputFormat.EmailAddress
            ? principal.FindFirst(ClaimTypes.Email)?.Value!
            : principal.FindFirst(ClaimTypes.MobilePhone)?.Value!;
    }

    private static async Task<User?> GetExistingUser(CosmosMethods cosmosMethods, string input, InputFormat format)
    {
        return format == InputFormat.EmailAddress
            ? await cosmosMethods.GetUserByEmailAddress(input)
            : await cosmosMethods.GetUserByPhoneNumber(input);
    }

    private static async Task UpdateUserLastSignedIn(Guid userId, DateTimeOffset currentDateTime, CosmosMethods cosmosMethods)
    {
        await cosmosMethods.UpdateLastSignedIn(userId, currentDateTime);
    }
    //private static async Task UpdateExistingUser(User user, ClaimsPrincipal principal, LoginProvider provider, string input, DateTimeOffset currentDateTime, CosmosMethods cosmosMethods)
    //{
    //    user.FirstName ??= principal.FindFirst(ClaimTypes.GivenName)?.Value ?? "--";
    //    user.LastName ??= principal.FindFirst(ClaimTypes.Surname)?.Value ?? "--";
    //    user.DisplayName ??= principal.FindFirst(ClaimTypes.Name)?.Value ?? $"{user.FirstName} {user.LastName}";

    //    LoginInput existingInput = user.Inputs.First(key => key.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
    //    if (!existingInput.Providers.Any(key => key.Code.Equals(provider.Code, StringComparison.OrdinalIgnoreCase)))
    //        existingInput.Providers.Add(provider);

    //    user.LastSignedIn = currentDateTime;
    //    await cosmosMethods.Update(user);
    //}

    //private async Task CreateNewUser(ClaimsPrincipal principal, LoginProvider provider, string input, InputFormat formatValue, DateTimeOffset currentDateTime, CosmosMethods cosmosMethods)
    //{
    //    (string? countryCode, string? callingCode, string formattedInput) = await ProcessPhoneNumber(formatValue, input);

    //    string firstName = principal.FindFirst(ClaimTypes.GivenName)?.Value ?? "--";
    //    string lastName = principal.FindFirst(ClaimTypes.Surname)?.Value ?? "--";

    //    User user = new()
    //    {
    //        ID = Guid.NewGuid(),
    //        FirstName = firstName,
    //        LastName = lastName,
    //        DisplayName = (principal.FindFirst(ClaimTypes.Name) ?? principal.FindFirst("name"))?.Value ?? $"{firstName} {lastName}",
    //        CreatedOn = currentDateTime,
    //        LastSignedIn = currentDateTime,
    //        Inputs =
    //        [
    //            new LoginInput()
    //            {
    //                Input = formattedInput,
    //                Format = formatValue,
    //                IsPrimary = true,
    //                PhoneNumberCountryCode = countryCode,
    //                PhoneNumberCallingCode = callingCode,
    //                Providers = provider != null ? new() { provider } : new()
    //            }
    //        ]
    //    };

    //    await cosmosMethods.Create(user);
    //}
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
