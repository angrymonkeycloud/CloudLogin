using System.Web;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http.Extensions;
using System.Collections.Specialized;

namespace AngryMonkey.CloudLogin;

[Route("Account")]
public class CloudLoginController : ControllerBase
{
    CloudLoginClient CloudLogin { get; set; }

    public CloudLoginController(CloudLoginClient cloudLogin) => CloudLogin = cloudLogin;

    [Route("Login")]
    public async Task<IActionResult> Login(string? ReturnUrl)
    {
        string baseUrl = $"{Request.Scheme}://{Request.Host}";
        string seperator = CloudLogin.LoginUrl.Contains('?') ? "&" : "?";

        if (string.IsNullOrEmpty(ReturnUrl))
        {
            ReturnUrl = Request.Headers["referer"];

            if (string.IsNullOrEmpty(ReturnUrl))
                ReturnUrl = baseUrl;
        }

        string redirectUri = $"{baseUrl}/Account/LoginResult?ReturnUrl={HttpUtility.UrlEncode(ReturnUrl)}";

        return Redirect($"{CloudLogin.LoginUrl}{seperator}redirectUri={HttpUtility.UrlEncode(redirectUri)}&actionState=login");
    }

    [Route("LoginResult")]
    public async Task<IActionResult> LoginResult(Guid requestId, string? ReturnUrl)
    {
        string seperator = CloudLogin.LoginUrl.Contains('?') ? "&" : "?";

        if (string.IsNullOrEmpty(ReturnUrl))
            ReturnUrl = $"{Request.Scheme}://{Request.Host}";

        if (requestId == Guid.Empty)
            return Redirect($"{CloudLogin.LoginUrl}{seperator}redirectUri={HttpUtility.UrlEncode(Request.GetEncodedUrl())}&actionState=login");

        User? cloudUser = await CloudLogin.GetUserByRequestId(requestId);

        if (cloudUser == null)
            return await Login(ReturnUrl);

        //Response.Cookies.Append("LoggedInUser", JsonConvert.SerializeObject(cloudUser));

        ClaimsIdentity claimsIdentity = new(new[] {
            new Claim(ClaimTypes.NameIdentifier, cloudUser.ID.ToString()),
            new Claim(ClaimTypes.GivenName, cloudUser.FirstName ?? string.Empty),
            new Claim(ClaimTypes.Surname, cloudUser.LastName ?? string.Empty),
            new Claim(ClaimTypes.Name, cloudUser.DisplayName ?? string.Empty),
            new Claim(ClaimTypes.Email, cloudUser.PrimaryEmailAddress.Input),
            new Claim(ClaimTypes.UserData, JsonConvert.SerializeObject(cloudUser))
        }, "CloudLogin");

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, new AuthenticationProperties()
        {
            ExpiresUtc = null,
            IsPersistent = false
        });

        return Redirect(ReturnUrl);
    }

    [Route("Logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();

        Response.Cookies.Delete("User");
        Response.Cookies.Delete("LoggedInUser");
        Response.Cookies.Delete("CloudLogin");

        var baseUri = $"{Request.Scheme}://{Request.Host}";

        string seperator = CloudLogin.LoginUrl.Contains('?') ? "&" : "?";

        return Redirect($"{CloudLogin.LoginUrl}CloudLogin/Logout{seperator}redirectUri={HttpUtility.UrlEncode(baseUri)}");
    }

    [Route("ChangePrimary")]
    public IActionResult ChangePrimary()
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        return Redirect($"{CloudLogin.LoginUrl}{HttpUtility.UrlEncode(baseUri)}/ChangePrimary");

    }

    [Route("AddInput")]
    public IActionResult AddInput()
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        return Redirect($"{CloudLogin.LoginUrl}{HttpUtility.UrlEncode(baseUri)}/AddInput");

    }

    [Route("Update")]
    public IActionResult Update()
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        return Redirect($"{CloudLogin.LoginUrl}{HttpUtility.UrlEncode(baseUri)}/UpdateInput");

    }
}