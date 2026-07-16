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

        if (user == null || user.IsLocked)
            return null;

        // Test accounts may only use the explicit test-login endpoint while test mode is enabled.
        if (user.IsTest)
            return null;

        LoginProvider? passwordProvider = user.Inputs
            .SelectMany(key => key.Providers)
            .FirstOrDefault(key => key.Code.Equals("password", StringComparison.OrdinalIgnoreCase));
        string? passwordHash = passwordProvider?.PasswordHash;

        if (passwordHash == null)
            return null;

        // Verify password
        if (VerifyPassword(password, passwordHash, out bool needsRehash))
        {
            if (needsRehash)
                passwordProvider!.PasswordHash = await HashPassword(password);

            // Update last signed in time
            user.LastSignedIn = DateTimeOffset.UtcNow;
            await UpdateUser(user);
            return user;
        }

        return null;
    }

    private bool VerifyPassword(string password, string hashedPassword, out bool needsRehash)
    {
        needsRehash = false;

        try
        {
            if (password.Length > _configuration.Security.MaximumPasswordLength)
                return false;

            if (hashedPassword.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal))
            {
                string[] parts = hashedPassword.Split('$');
                if (parts.Length != 4 ||
                    !int.TryParse(parts[1], out int iterations) ||
                    iterations < 100_000)
                    return false;

                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] storedHash = Convert.FromBase64String(parts[3]);
                if (salt.Length != 16 || storedHash.Length != 32)
                    return false;

                byte[] calculatedHash = KeyDerivation.Pbkdf2(
                    password,
                    salt,
                    KeyDerivationPrf.HMACSHA256,
                    iterations,
                    32);

                needsRehash = iterations < _configuration.Security.PasswordHashIterations;
                return CryptographicOperations.FixedTimeEquals(storedHash, calculatedHash);
            }

            // Backward-compatible verification for the former salt+hash format.
            byte[] fullHash = Convert.FromBase64String(hashedPassword);

            if (fullHash.Length != 48) // 16 bytes salt + 32 bytes hash
                return false;

            byte[] legacySalt = [.. fullHash.Take(16)];
            byte[] legacyStoredHash = [.. fullHash.Skip(16)];

            byte[] testHash = KeyDerivation.Pbkdf2(
                password,
                legacySalt,
                KeyDerivationPrf.HMACSHA256,
                iterationCount: 100000, // Match the iteration count from HashPassword
                numBytesRequested: 32);

            bool valid = CryptographicOperations.FixedTimeEquals(legacyStoredHash, testHash);
            needsRehash = valid;
            return valid;
        }
        catch { return false; }
    }
}
