using System.Web;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Text.Json;

namespace AngryMonkey.CloudLogin;

[Route("Account")]
[ApiController]
public class CloudLoginController(CloudLoginClient cloudLogin) : Controller
{
    CloudLoginClient CloudLogin { get; set; } = cloudLogin;

    [Route("Login")]
    public IActionResult Login(string? ReturnUrl)
    {
        string baseUrl = $"{Request.Scheme}://{Request.Host}";
        string seperator = CloudLogin.LoginUrl.Contains('?') ? "&" : "?";

        if (string.IsNullOrEmpty(ReturnUrl))
            ReturnUrl = baseUrl;

        string redirectUri = $"{baseUrl}/Account/LoginResult?ReturnUrl={HttpUtility.UrlEncode(ReturnUrl)}";

        return Redirect($"{CloudLogin.LoginUrl}{seperator}redirectUri={HttpUtility.UrlEncode(redirectUri)}&actionState=login");
    }

    [Route("LoginResult")]
    public async Task<IActionResult> LoginResult(Guid requestId, string? currentUser, string? ReturnUrl, bool KeepMeSignedIn)
    {
        string seperator = CloudLogin.LoginUrl.Contains('?') ? "&" : "?";

        if (string.IsNullOrEmpty(ReturnUrl))
            ReturnUrl = $"{Request.Scheme}://{Request.Host}";

        User? cloudUser = null;

        if (requestId == Guid.Empty)
        {
            if (!string.IsNullOrEmpty(currentUser))
                cloudUser = JsonSerializer.Deserialize<User>(currentUser, CloudLoginSerialization.Options);
            else
                return Redirect($"{CloudLogin.LoginUrl}{seperator}redirectUri={HttpUtility.UrlEncode(Request.GetEncodedUrl())}&actionState=login");
        }

        if (cloudUser == null)
            cloudUser = await CloudLogin.GetUserByRequestId(requestId);

        if (cloudUser == null)
            return Login(ReturnUrl);


        //Response.Cookies.Append("LoggedInUser", JsonConvert.SerializeObject(cloudUser));

        ClaimsIdentity claimsIdentity = new([
            new Claim(ClaimTypes.NameIdentifier, cloudUser.ID.ToString()),
            new Claim(ClaimTypes.GivenName, cloudUser.FirstName ?? string.Empty),
            new Claim(ClaimTypes.Surname, cloudUser.LastName ?? string.Empty),
            new Claim(ClaimTypes.Name, cloudUser.DisplayName ?? string.Empty),
            new Claim(ClaimTypes.UserData, JsonSerializer.Serialize(cloudUser, CloudLoginSerialization.Options))
        ], "CloudLogin");

        if (cloudUser.PrimaryEmailAddress != null)
            claimsIdentity.AddClaim(new(ClaimTypes.Email, cloudUser.PrimaryEmailAddress.Input));
        else
            claimsIdentity.AddClaim(new(ClaimTypes.Email, cloudUser.Inputs.First().Input));

        ClaimsPrincipal claimsPrincipal = new(claimsIdentity);

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = null,
            IsPersistent = false
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, properties);

        Response.Cookies.Delete("LoggingIn");

        if (KeepMeSignedIn)
            Response.Cookies.Append("AutomaticSignIn", "True", new() { Expires = DateTime.MaxValue });

        return Redirect(ReturnUrl);
    }

    [Route("Logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();

        //Response.Cookies.Delete("User");
        //Response.Cookies.Delete("LoggedInUser");
        Response.Cookies.Delete("AutomaticSignIn");
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

    [HttpGet("CurrentUser")]
    public ActionResult<User?> CurrentUser()
    {
        try
        {
            string? userCookie = Request.Cookies["CloudLogin"];

            if (userCookie == null)
                return Ok(null);

            ClaimsIdentity userIdentity = Request.HttpContext.User.Identities.First();

            string? loginIdentity = userIdentity.FindFirst(ClaimTypes.UserData)?.Value;

            if (string.IsNullOrEmpty(loginIdentity))
                return Ok(null);

            User? user = JsonSerializer.Deserialize<User?>(loginIdentity, CloudLoginSerialization.Options);

            if (user == null)
                return Ok(null);

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("IsAuthenticated")]
    public ActionResult<bool> IsAuthenticated()
    {
        try
        {
            string? userCookie = Request.Cookies["CloudLogin"];
            return Ok(userCookie != null);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("AutomaticLogin")]
    public ActionResult<bool> AutomaticLogin()
    {
        try
        {
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            string seperator = CloudLogin.LoginUrl.Contains('?') ? "&" : "?";

            string? userCookie = Request.Cookies["AutomaticSignIn"];

            if (userCookie == null)
                return false;
            else
                return Redirect($"{CloudLogin.LoginUrl}{seperator}Account/Login");
        }
        catch
        {
            return Problem();
        }
    }
}