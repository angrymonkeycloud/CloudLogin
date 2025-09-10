using System.Web;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    public async Task<IActionResult> LoginResult(HttpRequest request, HttpResponse response, Guid requestId, string? currentUser, string? returnUrl, bool keepMeSignedIn)
    {
        string separator = LoginUrl.Contains('?') ? "&" : "?";

        if (string.IsNullOrEmpty(returnUrl))
            returnUrl = $"{request.Scheme}://{request.Host}";

        User? cloudUser = null;

        if (requestId == Guid.Empty)
        {
            if (!string.IsNullOrEmpty(currentUser))
                cloudUser = JsonSerializer.Deserialize<User>(currentUser, CloudLoginSerialization.Options);
            else
                return new RedirectResult($"{LoginUrl}{separator}ReturnUrl={HttpUtility.UrlEncode(returnUrl ?? request.GetEncodedUrl())}&actionState=login");

            //return new RedirectResult($"{LoginUrl}{separator}redirectUri={HttpUtility.UrlEncode(returnUrl ?? request.GetEncodedUrl())}&actionState=login");
        }

        if (cloudUser == null)
            cloudUser = await GetUserByRequestId(requestId);

        if (cloudUser == null)
            return Login(request, returnUrl);

        ClaimsIdentity claimsIdentity = new(
        [
            new Claim(ClaimTypes.NameIdentifier, cloudUser.ID.ToString()),
            new Claim(ClaimTypes.GivenName, cloudUser.FirstName ?? string.Empty),
            new Claim(ClaimTypes.Surname, cloudUser.LastName ?? string.Empty),
            new Claim(ClaimTypes.Name, cloudUser.DisplayName ?? string.Empty),
            new Claim(ClaimTypes.UserData, JsonSerializer.Serialize(cloudUser, CloudLoginSerialization.Options))
        ], "CloudLogin");

        if (cloudUser.PrimaryEmailAddress != null)
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, cloudUser.PrimaryEmailAddress.Input));
        else
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, cloudUser.Inputs.First().Input));

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = null,
            IsPersistent = false
        };

        await request.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, properties);

        response.Cookies.Delete("LoggingIn");

        if (keepMeSignedIn)
            response.Cookies.Append("AutomaticSignIn", "True", new CookieOptions { Expires = DateTime.MaxValue });

        return new RedirectResult(returnUrl);
    }

    public ActionResult<bool> AutomaticLogin(HttpRequest request)
    {
        try
        {
            string baseUrl = $"{request.Scheme}://{request.Host}";
            string separator = LoginUrl.Contains('?') ? "&" : "?";

            string? userCookie = request.Cookies["AutomaticSignIn"];

            if (userCookie == null)
                return new OkObjectResult(false);
            else
                return new RedirectResult($"{LoginUrl}{separator}Account/Login");
        }
        catch
        {
            return new ObjectResult("An error occurred while attempting automatic login.") { StatusCode = 500 };
        }
    }

    public async Task<string> Logout(HttpRequest request, HttpResponse response)
    {
        await request.HttpContext.SignOutAsync();

        response.Cookies.Delete("AutomaticSignIn");
        var baseUri = $"{request.Scheme}://{request.Host}";

        string separator = LoginUrl.Contains('?') ? "&" : "?";

        return $"{LoginUrl}CloudLogin/Logout{separator}redirectUri={HttpUtility.UrlEncode(baseUri)}";
    }

    public string ChangePrimary()
    {
        var baseUri = $"{_request.Scheme}://{_request.Host}";

        return $"{LoginUrl}{HttpUtility.UrlEncode(baseUri)}/ChangePrimary";
    }

    public async Task<string> SetPrimary(string input, string domainName)
    {
        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host.Value}";

        User? user = await _cosmosMethods.GetUserByEmailAddress(input);

        if (user == null)
            return $"{baseUrl}/CloudLogin/Update?redirectUri={domainName}";

        user.Inputs.First(i => i.IsPrimary).IsPrimary = false;
        user.Inputs.First(i => i.Input == input).IsPrimary = true;

        await _cosmosMethods.Update(user);

        string userSerialized = JsonSerializer.Serialize(user, CloudLoginSerialization.Options);

        return $"{baseUrl}/CloudLogin/Update?redirectUri={domainName}&userInfo={HttpUtility.UrlEncode(userSerialized)}";
    }

    //public async Task AddInput(Guid userId, LoginInput Input)
    //{
    //    User user = await GetUserById(userId) ?? throw new Exception("User not found.");

    //    user.Inputs.Add(Input);

    //    await _cosmosMethods._container.UpsertItemAsync(user);
    //}

    public async Task<string> AddInput(string redirectUrl, string userInfo, string primaryEmail)
    {
        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host.Value}";

        LoginInput? input = JsonSerializer.Deserialize<LoginInput>(userInfo, CloudLoginSerialization.Options);

        input.IsPrimary = false;

        User? user = await _cosmosMethods.GetUserByEmailAddress(primaryEmail);
        User? oldUser = await _cosmosMethods.GetUserByEmailAddress(input.Input);

        if (oldUser != null)
            return $"{baseUrl}/CloudLogin/Update?redirectUri={redirectUrl}";

        oldUser = await _cosmosMethods.GetUserByPhoneNumber(input.Input);

        if (oldUser != null)
            return $"{baseUrl}/CloudLogin/Update?redirectUri={redirectUrl}";

        user.Inputs.Add(input);

        await _cosmosMethods.AddInput(user.ID, input);
        string redirectTo = redirectUrl.Split("/").Last().Replace("AddInput", "");
        redirectUrl = redirectUrl.Replace(redirectUrl.Split("/").Last(), "");

        string userSerialized = JsonSerializer.Serialize(user, CloudLoginSerialization.Options);

        return $"{redirectUrl}CloudLogin/Update?redirectUri={redirectTo}&userInfo={HttpUtility.UrlEncode(userSerialized)}";
    }

    public string GetAddInputUrl()
    {
        var baseUri = $"{_request.Scheme}://{_request.Host}";

        return Path.Combine(HttpUtility.UrlEncode(baseUri), "AddInput");
    }

    public string GetUpdateUrl()
    {
        var baseUri = $"{_request.Scheme}://{_request.Host}";

        return Path.Combine(HttpUtility.UrlEncode(baseUri), "UpdateInput");
    }

    public async Task<string> Update(string userInfo, string domainName)
    {
        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host.Value}";

        Dictionary<string, string>? userDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(userInfo, CloudLoginSerialization.Options);

        string? firstName = userDictionary?["FirstName"];
        string? lastName = userDictionary?["LastName"];
        string? displayName = userDictionary?["DisplayName"];
        string? userID = userDictionary?["UserId"];

        User? user = await _cosmosMethods.GetUserById(new Guid(userID));

        user.FirstName = firstName;
        user.LastName = lastName;
        user.DisplayName = displayName;

        await _cosmosMethods.Update(user);

        string userSerialized = JsonSerializer.Serialize(user, CloudLoginSerialization.Options);

        return $"{baseUrl}/CloudLogin/Update?redirectUri={domainName}&userInfo={HttpUtility.UrlEncode(userSerialized)}";
    }
}
