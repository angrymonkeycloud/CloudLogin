//using System.Security.Cryptography;
//using Microsoft.AspNetCore.Cryptography.KeyDerivation;

//namespace AngryMonkey.CloudLogin.Server;

//public partial class CloudLoginServer
//{
//    public async Task<User> RegisterEmailPasswordUser(string email, string password, string firstName, string lastName)
//    {
//        // Ensure user doesn't already exist
//        User? existing = await GetUserByEmailAddress(email);

//        if (existing != null)
//            throw new Exception("User already exists.");

//        User newUser = new()
//        {
//            ID = Guid.NewGuid(),
//            FirstName = firstName,
//            LastName = lastName,
//            DisplayName = firstName + " " + lastName,
//            CreatedOn = DateTimeOffset.UtcNow,
//            LastSignedIn = DateTimeOffset.UtcNow,
//            Inputs = [new() { 
//                Input = email, 
//                Format = InputFormat.EmailAddress, 
//                IsPrimary = true, 
//                PasswordHash = await HashPassword(password),
//                Providers = [new() { Code = "Password" }]
//            }]
//        };

//        await CreateUser(newUser);

//        return newUser;
//    }
//}
