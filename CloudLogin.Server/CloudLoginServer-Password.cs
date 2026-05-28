using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Security.Cryptography;
using AngryMonkey.CloudLogin.Sever.Providers;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    public async Task<UserModel?> ValidateEmailPassword(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;

        // Normalize email
        email = email.Trim().ToLowerInvariant();

        UserModel? user = await GetUserByEmailAddress(email);

        if (user == null)
            return null;

        // Test-mode fast-path: a user flagged as test authenticates against the shared
        // password configured on the LoginTestProviders.PasswordProviderTestConfiguration.
        LoginTestProviders.PasswordProviderTestConfiguration? testProvider = _configuration.Providers
            .OfType<LoginTestProviders.PasswordProviderTestConfiguration>()
            .FirstOrDefault();

        if (user.IsTest
            && testProvider?.IsTestEnabled == true
            && string.Equals(password, testProvider.Password, StringComparison.Ordinal))
        {
            user.LastSignedIn = DateTimeOffset.UtcNow;
            await UpdateUser(user);
            return user;
        }

        string? passwordHash = user.Inputs.SelectMany(key => key.Providers).FirstOrDefault(key => key.Code.Equals("password", StringComparison.OrdinalIgnoreCase))?.PasswordHash;

        if (passwordHash == null)
            return null;

        // Verify password
        if (VerifyPassword(password, passwordHash))
        {
            // Update last signed in time
            user.LastSignedIn = DateTimeOffset.UtcNow;
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