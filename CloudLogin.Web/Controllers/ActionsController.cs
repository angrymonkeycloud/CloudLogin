using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Web;

namespace AngryMonkey.CloudLogin;
[Route("CloudLogin/Actions")]
[ApiController]
public class ActionsController(CloudLoginConfiguration configuration, CosmosMethods? cosmosMethods = null) : CloudLoginBaseController(configuration, cosmosMethods)
{
    [HttpGet("AddInput")]
    public async Task<ActionResult> AddInput(string redirectUrl, string userInfo, string primaryEmail)
    {
        if (CosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        LoginInput? input = JsonSerializer.Deserialize<LoginInput>(userInfo, CloudLoginSerialization.Options);

        input.IsPrimary = false;

        User? user = await CosmosMethods.GetUserByEmailAddress(primaryEmail);
        User? oldUser = await CosmosMethods.GetUserByEmailAddress(input.Input);

        if (oldUser != null)
            return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={redirectUrl}");

        oldUser = await CosmosMethods.GetUserByPhoneNumber(input.Input);

        if (oldUser != null)
            return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={redirectUrl}");

        user.Inputs.Add(input);

        await CosmosMethods.AddInput(user.ID, input);
        string redirectTo = redirectUrl.Split("/").Last().Replace("AddInput", "");
        redirectUrl = redirectUrl.Replace(redirectUrl.Split("/").Last(), "");

        string userSerialized = JsonSerializer.Serialize(user, CloudLoginSerialization.Options);

        return Redirect($"{redirectUrl}CloudLogin/Update?redirectUri={redirectTo}&userInfo={HttpUtility.UrlEncode(userSerialized)}");
    }

    [HttpGet("Update")]
    public async Task<ActionResult> Update(string userInfo, string domainName)
    {
        if (CosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        Dictionary<string, string>? userDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(userInfo, CloudLoginSerialization.Options);

        string? firstName = userDictionary?["FirstName"];
        string? lastName = userDictionary?["LastName"];
        string? displayName = userDictionary?["DisplayName"];
        string? userID = userDictionary?["UserId"];

        User? user = await CosmosMethods.GetUserById(new Guid(userID));

        user.FirstName = firstName;
        user.LastName = lastName;
        user.DisplayName = displayName;

        await CosmosMethods.Update(user);

        string userSerialized = JsonSerializer.Serialize(user, CloudLoginSerialization.Options);

        return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={domainName}&userInfo={HttpUtility.UrlEncode(userSerialized)}");
    }

    [HttpGet("SetPrimary")]
    public async Task<ActionResult> SetPrimary(string input, string domainName)
    {
        if (CosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

        User? user = await CosmosMethods.GetUserByEmailAddress(input);

        if (user == null)
            return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={domainName}");

        user.Inputs.First(i => i.IsPrimary).IsPrimary = false;
        user.Inputs.First(i => i.Input == input).IsPrimary = true;

        await CosmosMethods.Update(user);

        string userSerialized = JsonSerializer.Serialize(user, CloudLoginSerialization.Options);

        return Redirect($"{baseUrl}/CloudLogin/Update?redirectUri={domainName}&userInfo={HttpUtility.UrlEncode(userSerialized)}");
    }
}
