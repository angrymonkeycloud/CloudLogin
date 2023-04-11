using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using System.Web;

namespace AngryMonkey.CloudLogin;
[Route("CloudLogin/Actions")]
[ApiController]
public class ActionsController : BaseController
{
    [HttpGet("AddInput")] 
    public async Task<ActionResult> AddInput(string domainName, string userInfo, string primaryEmail)
    {
        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        LoginInput input = JsonConvert.DeserializeObject<LoginInput>(userInfo);

        input.IsPrimary = false;

        User user = await CosmosMethods.GetUserByEmailAddress(primaryEmail);
        User oldUser = await CosmosMethods.GetUserByEmailAddress(input.Input);

        if (oldUser != null)
            return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={domainName}");

        oldUser = await CosmosMethods.GetUserByPhoneNumber(input.Input);

        if (oldUser != null)
            return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={domainName}");

        user.Inputs.Add(input);

        await CosmosMethods.AddInput(user.ID, input);
        string redirectTo = domainName.Split("/").Last().Replace("AddInput", "");
        domainName = domainName.Replace(domainName.Split("/").Last(), "");

        string userSerialized = JsonConvert.SerializeObject(user);

        return Redirect($"{domainName}CloudLogin/Update?redirectUri={redirectTo}&userInfo={HttpUtility.UrlEncode(userSerialized)}");
    }

    [HttpGet("Update")]
    public async Task<ActionResult> Update(string userInfo, string domainName)
    {
        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        Dictionary<string, string> userDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(userInfo);

        string firstName = userDictionary["FirstName"];
        string lastName = userDictionary["LastName"];
        string displayName = userDictionary["DisplayName"];
        string userID = userDictionary["UserId"];

        User user = await CosmosMethods.GetUserById(new Guid(userID));

        user.FirstName = firstName;
        user.LastName = lastName;
        user.DisplayName = displayName;

        await CosmosMethods.Container.UpsertItemAsync(user);

        string userSerialized = JsonConvert.SerializeObject(user);

        return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={domainName}&userInfo={HttpUtility.UrlEncode(userSerialized)}");
    }

    [HttpGet("SetPrimary")]
    public async Task<ActionResult> SetPrimary(string input, string domainName)
    {
        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        User? user = await CosmosMethods.GetUserByEmailAddress(input);

        if(user == null)
            return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={domainName}");

        user.Inputs.First(i => i.IsPrimary).IsPrimary = false;
        user.Inputs.First(i => i.Input == input).IsPrimary = true;

        await CosmosMethods.Container.UpsertItemAsync(user);

        string userSerialized = JsonConvert.SerializeObject(user);

        return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={domainName}&userInfo={HttpUtility.UrlEncode(userSerialized)}");
    }
}
