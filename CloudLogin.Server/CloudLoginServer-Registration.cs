using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    public async Task<User> RegisterEmailPasswordUser(string email, string password, string firstName, string lastName)
    {
        // Ensure user doesn't already exist
        User? existing = await GetUserByEmailAddress(email);

        if (existing != null)
            throw new Exception("User already exists.");

        User newUser = new()
        {
            ID = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            DisplayName = firstName + " " + lastName,
            PasswordHash = await HashPassword(password),
            Inputs = [new() { Input = email, Format = InputFormat.EmailAddress, IsPrimary = true }]
        };

        await CreateUser(newUser);

        return newUser;
    }
    public async Task<User?> ValidateEmailPassword(string email, string password)
    {
        User? user = await GetUserByEmailAddress(email);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
            return null;

        return CheckPassword(password, user.PasswordHash) ? user : null;
    }

   

    private static bool CheckPassword(string providedPassword, string storedHash)
    {
        // Reverse the base64(salt + hash)
        byte[] fullHash = Convert.FromBase64String(storedHash);

        byte[] salt = [.. fullHash.Take(16)];
        byte[] actualHash = [.. fullHash.Skip(16)];

        // Hash the incoming password with the same salt
        byte[] check = KeyDerivation.Pbkdf2(
            providedPassword,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount: 10000,
            numBytesRequested: 32);

        return check.SequenceEqual(actualHash);
    }
}
