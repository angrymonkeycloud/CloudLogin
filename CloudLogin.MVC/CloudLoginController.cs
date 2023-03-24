using System.Web;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace AngryMonkey.CloudLogin;
public class CloudLoginController : ControllerBase
{
    CloudLoginClient CloudLogin;
    public CloudLoginController(CloudLoginClient cloudLogin) => CloudLogin = cloudLogin;

    [Route("login")]
    public async Task<IActionResult> Login(Guid requestId)
    {
        var baseUri = $"{Request.Scheme}://{Request.Host}";

        if (requestId == Guid.Empty)
            return Redirect($"{CloudLogin.LoginUrl}?domainName={HttpUtility.UrlEncode(baseUri)}&actionState=login");

        User? User = await CloudLogin.GetUserByRequestId(requestId, 1);

        if (User == null)
            return await Login(Guid.Empty);

        Response.Cookies.Append("LoggedInUser", JsonConvert.SerializeObject(User));

        ClaimsIdentity claimsIdentity = new(new[] {
            new Claim(ClaimTypes.NameIdentifier, User.ID.ToString()),
            new Claim(ClaimTypes.GivenName, User.FirstName),
            new Claim(ClaimTypes.Surname, User.LastName),
            new Claim(ClaimTypes.Name, User.DisplayName)
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