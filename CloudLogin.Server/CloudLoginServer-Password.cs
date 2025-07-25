using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Security.Cryptography;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    public async Task<bool> PasswordLogin(string email, string password, bool keepMeSignedIn)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return false;

        User? user = await ValidateEmailPassword(email, password);
        if (user == null)
            return false;

        // Create claims for the authenticated user
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ID.ToString()),
            new(ClaimTypes.Email, email.ToLowerInvariant()),
            new(ClaimTypes.Name, user.DisplayName ?? $"{user.FirstName} {user.LastName}"),
            new(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
            new(ClaimTypes.Surname, user.LastName ?? string.Empty),
            new(ClaimTypes.UserData, JsonSerializer.Serialize(user, CloudLoginSerialization.Options))
        };

        var identity = new ClaimsIdentity(claims, "Password");
        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties
        {
            IsPersistent = keepMeSignedIn,
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.AddDays(30) : null
        };

        await _accessor.HttpContext!.SignInAsync(principal, properties);
        return true;
    }

    public async Task<User?> ValidateEmailPassword(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;

        // Normalize email
        email = email.Trim().ToLowerInvariant();

        User? user = await GetUserByEmailAddress(email);

        string? passwordHash = user?.Inputs.FirstOrDefault(key => !string.IsNullOrEmpty(key.PasswordHash))?.PasswordHash;

        if (passwordHash == null)
            return null;

        // Verify password
        if (VerifyPassword(password, passwordHash))
        {
            // Update last signed in time
            user!.LastSignedIn = DateTimeOffset.UtcNow;
            await UpdateUser(user);
            return user;
        }

        return null;
    }

    private static bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            byte[] fullHash = Convert.FromBase64String(hashedPassword);

            if (fullHash.Length != 48) // 16 bytes salt + 32 bytes hash
                return false;

            byte[] salt = fullHash.Take(16).ToArray();
            byte[] storedHash = fullHash.Skip(16).ToArray();

            byte[] testHash = KeyDerivation.Pbkdf2(
                password,
                salt,
                KeyDerivationPrf.HMACSHA256,
                iterationCount: 100000, // Match the iteration count from HashPassword
                numBytesRequested: 32);

            return CryptographicOperations.FixedTimeEquals(storedHash, testHash);
        }
        catch { return false; }
    }
}