using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Server;
using System.Linq;
using System.Collections.Generic;

public class DataParse
{
    public static UserInfo? Parse(UserModel? user)
    {
        if (user == null)
            return null;

        UserInfo userInformation = new()
        {
            DisplayName = user.DisplayName,
            FirstName = user.FirstName,
            IsLocked = user.IsLocked,
            LastName = user.LastName,
            CreatedOn = user.CreatedOn,
            DateOfBirth = user.DateOfBirth,
            LastSignedIn = user.LastSignedIn,
            Inputs = user.Inputs,
            Username = user.Username,
            // Added profile fields
            ProfilePicture = user.ProfilePicture,
            Country = user.Country,
            Locale = user.Locale
        };

        userInformation.SetId(user.ID);

        return userInformation;
    }

    public static List<UserModel>? Parse(List<UserInfo> Users)
    {
        if (Users == null)
            return [];

        return Users.Select(Parse).Where(user => user != null).ToList()!;
    }

    public static UserModel? Parse(UserInfo? dbUser)
    {
        if (dbUser == null)
            return null;

        // Ensure ID is properly parsed from the lowercase 'id' field
        dbUser.ProcessExtensionData();

        return new()
        {
            ID = dbUser.GetId(),
            DisplayName = dbUser.DisplayName,
            FirstName = dbUser.FirstName,
            IsLocked = dbUser.IsLocked,
            LastName = dbUser.LastName,
            CreatedOn = dbUser.CreatedOn,
            DateOfBirth = dbUser.DateOfBirth,
            LastSignedIn = dbUser.LastSignedIn,
            Inputs = dbUser.Inputs,
            Username = dbUser.Username,
            // Added profile fields
            ProfilePicture = dbUser.ProfilePicture,
            Country = dbUser.Country,
            Locale = dbUser.Locale
        };
    }

    public static List<UserInfo> Parse(List<UserModel> Users)
    {
        if (Users == null)
            return [];

        return Users.Select(Parse).Where(user => user != null).ToList()!;
    }
}