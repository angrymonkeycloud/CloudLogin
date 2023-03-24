namespace AngryMonkey.CloudLogin;
public class DataParse
{
    public static DbUser? Parse(User? User)
    {
        if (User == null)
            return null;
        DbUser userInformation = new()
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
            Username = User.Username,
            Discriminator = "User",
            PartitionKey = "User",
        };
        return userInformation;
    }
    public List<DbUser>? Parse(List<User> Users)
    {
        if (Users == null)
            return null;

        return Users.Select(Parse).ToList();
    }
    public static User? Parse(DbUser? dbUser)
    {
        if (dbUser == null)
            return null;
        User userInformation = new()
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
        return userInformation;
    }
    public List<User>? Parse(List<DbUser> Users)
    {
        if (Users == null)
            return null;

        return Users.Select(Parse).ToList();
    }
}