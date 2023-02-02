using AngryMonkey.CloudLogin.DataContract;
using AngryMonkey.CloudLogin.Models;
using System.Collections.Generic;

namespace AngryMonkey.CloudLogin;

public class CloudLoginClientBase
{

    internal UserModel? Parse(CloudUser cloudUser)
    {
        UserModel userInformation = new()
        {
            DisplayName= cloudUser.DisplayName,
            FirstName= cloudUser.FirstName,
            IsLocked= cloudUser.IsLocked,
            LastName= cloudUser.LastName
        };

        return userInformation;
    }

    internal List<UserModel>? Parse(List<CloudUser> cloudUsers)
    {
        if (cloudUsers == null)
            return null;

        return cloudUsers.Select(Parse).ToList();
    }
}