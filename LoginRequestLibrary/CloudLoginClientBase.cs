using AngryMonkey.Cloud.Login.DataContract;
using AngryMonkey.Cloud.Login.Models;
using System.Collections.Generic;

namespace LoginRequestLibrary;
public class CloudLoginClientBase
{

    internal UserInformation? Parse(CloudUser cloudUser)
    {
        UserInformation userInformation = new()
        {
            DateOfBirth= cloudUser.DateOfBirth,
            DisplayName= cloudUser.DisplayName,
            FirstName= cloudUser.FirstName,
            IsLocked= cloudUser.IsLocked,
            LastName= cloudUser.LastName
        };

        return userInformation;
    }

    internal List<UserInformation>? Parse(List<CloudUser> cloudUsers)
    {
        if (cloudUsers == null)
            return null;

        return cloudUsers.Select(Parse).ToList();
    }
}