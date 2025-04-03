namespace AngryMonkey.CloudLogin.Server;

public class DataParse
{
    public static DataUser? Parse(User? user)
    {
        if (user == null)
            return null;

        DataUser userInformation = new()
        {
            ID = user.ID,
            DisplayName = user.DisplayName,
            FirstName = user.FirstName,
            IsLocked = user.IsLocked,
            LastName = user.LastName,
            CreatedOn = user.CreatedOn,
            DateOfBirth = user.DateOfBirth,
            LastSignedIn = user.LastSignedIn,
            Inputs = user.Inputs,
            Username = user.Username
        };

        return userInformation;
    }

    public static List<User>? Parse(List<DataUser> Users)
    {
        if (Users == null)
            return [];

        return Users.Select(Parse).ToList();
    }

    public static User? Parse(DataUser? dbUser)
    {
        if (dbUser == null)
            return null;

        return new()
        {
            ID = dbUser.ID,
            DisplayName = dbUser.DisplayName,
            FirstName = dbUser.FirstName,
            IsLocked = dbUser.IsLocked,
            LastName = dbUser.LastName,
            CreatedOn = dbUser.CreatedOn,
            DateOfBirth = dbUser.DateOfBirth,
            LastSignedIn = dbUser.LastSignedIn,
            Inputs = dbUser.Inputs,
            Username = dbUser.Username
        };
    }

    public static List<DataUser> Parse(List<User> Users)
    {
        if (Users == null)
            return [];

        return Users.Select(Parse).Where(key => key != null).ToList();
    }
}