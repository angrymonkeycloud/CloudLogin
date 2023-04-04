using System.Web;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace AngryMonkey.CloudLogin;

public class CloudLoginController : ControllerBase
{
    CloudLoginClient CloudLogin { get; set; }

    public CloudLoginController(CloudLoginClient cloudLogin) => CloudLogin = cloudLogin;

    [Route("login")]
    public async Task<IActionResult> Login(Guid requestId)
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        if (requestId == Guid.Empty)
            return Redirect($"{CloudLogin.LoginUrl}?domainName={HttpUtility.UrlEncode(baseUri)}&actionState=login");

        User? cloudUser = await CloudLogin.GetUserByRequestId(requestId);

        if (cloudUser == null)
            return await Login(Guid.Empty);

        //Response.Cookies.Append("LoggedInUser", JsonConvert.SerializeObject(cloudUser));

        ClaimsIdentity claimsIdentity = new(new[] {
            new Claim(ClaimTypes.NameIdentifier, cloudUser.ID.ToString()),
            new Claim(ClaimTypes.GivenName, cloudUser.FirstName),
            new Claim(ClaimTypes.Surname, cloudUser.LastName),
            new Claim(ClaimTypes.Name, cloudUser.DisplayName),
            new Claim(ClaimTypes.Email, cloudUser.PrimaryEmailAddress.Input),
            new Claim(ClaimTypes.UserData, JsonConvert.SerializeObject(cloudUser))
        }, "CloudLogin");

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, new AuthenticationProperties()
        {
            ExpiresUtc = null,
            IsPersistent = false
        });

        return Redirect(baseUri);
    }
    [Route("logout")]
    public IActionResult logout()
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        return Redirect($"{CloudLogin.LoginUrl}CloudLogin/Logout?redirectUrl={HttpUtility.UrlEncode(baseUri)}");
    }
    [Route("changeprimary")]
    public IActionResult ChangePrimary()
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        return Redirect($"{CloudLogin.LoginUrl}{HttpUtility.UrlEncode(baseUri)}/ChangePrimary");

    }
    [Route("addinput")]
    public IActionResult AddInput()
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        return Redirect($"{CloudLogin.LoginUrl}{HttpUtility.UrlEncode(baseUri)}/AddInput");

    }
    [Route("update")]
    public IActionResult Update()
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        return Redirect($"{CloudLogin.LoginUrl}{HttpUtility.UrlEncode(baseUri)}/UpdateInput");

    }
}