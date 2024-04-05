namespace AngryMonkey.CloudLogin;

public class DataParse
{
    public static Data.User? Parse(User? User)
    {
        if (User == null)
            return null;

        Data.User userInformation = new()
        {
            ID = User.ID,
            DisplayName = User.DisplayName,
            FirstName = User.FirstName,
            IsLocked = User.IsLocked,
            LastName = User.LastName,
            CreatedOn = User.CreatedOn,
            DateOfBirth = User.DateOfBirth,
            LastSignedIn = User.LastSignedIn,
            Inputs = User.Inputs,
            Username = User.Username
        };

        return userInformation;
    }

    public static List<Data.User>? Parse(List<User> Users)
    {
        if (Users == null)
            return [];

        return Users.Select(Parse).ToList();
    }

    public static User? Parse(Data.User? dbUser)
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

    public static List<User> Parse(List<Data.User> Users)
    {
        if (Users == null)
            return [];

        return Users.Select(Parse).Where(key => key != null).ToList();
    }
}