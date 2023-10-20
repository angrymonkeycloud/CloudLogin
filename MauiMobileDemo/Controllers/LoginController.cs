//using Microsoft.AspNetCore.Mvc;
//using System.Security.Claims;
//using System.Web.Mvc;
//using HttpGetAttribute = Microsoft.AspNetCore.Mvc.HttpGetAttribute;
//using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

//namespace MauiMobileDemo.Controllers;

//[Route("CloudLogin")]
//[ApiController]
//public class LoginController : Controller
//{
//    [HttpGet("Result")]
//    public async Task<ActionResult<string>> LoginResult(bool keepMeSignedIn, bool sameSite, string? redirectUri = null, string actionState = "", string primaryEmail = "")
//    {
//        var context = HttpContext;
//        var Request = HttpContext.Request;
//        ClaimsIdentity userIdentity = Request.HttpContext.User.Identities.First();

//        User? user = new();

//        if (Configuration.Cosmos != null)
//            user = CosmosMethods.GetUserByInput(userIdentity.FindFirst(ClaimTypes.Email)?.Value!).Result;

//        string baseUrl = $"http{(Request.IsHttps ? "s" : string.Empty)}://{Request.Host.Value}";

//        redirectUri ??= baseUrl;

//        AuthenticationProperties properties = new()
//        {
//            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(Configuration.LoginDuration) : null,
//            IsPersistent = keepMeSignedIn
//        };


//        string firstName = user.FirstName ??= userIdentity.FindFirst(ClaimTypes.GivenName)?.Value;
//        string lastName = user.LastName ??= userIdentity.FindFirst(ClaimTypes.Surname)?.Value;
//        string emailaddress = userIdentity.FindFirst(ClaimTypes.Email)?.Value;
//        string displayName = user.DisplayName ??= $"{firstName} {lastName}";

//        if (Configuration.Cosmos == null)
//            user = new()
//            {
//                DisplayName = displayName,
//                FirstName = firstName,
//                LastName = lastName,
//                ID = Guid.NewGuid(),
//                Inputs = new()
//                {
//                    new()
//                    {
//                        Format = InputFormat.EmailAddress,
//                        Input = emailaddress,
//                        IsPrimary = true
//                    }
//                }
//            };


//        if (user == null)
//            return Redirect(redirectUri);





//        ClaimsIdentity claimsIdentity = new(new[] {
//                //new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
//                //new Claim(ClaimTypes.GivenName, firstName),
//                //new Claim(ClaimTypes.Surname, lastName),
//                //new Claim(ClaimTypes.Name, displayName),
//                new Claim(ClaimTypes.Hash, "CloudLogin"),
//                new Claim(ClaimTypes.UserData, JsonConvert.SerializeObject(user))
//            }, "CloudLogin");

//        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);


//        if (actionState == "AddInput")
//        {
//            LoginInput input = user.Inputs.First();
//            string userInfo = JsonConvert.SerializeObject(input);

//            return Redirect(Methods.RedirectString("Actions", "AddInput", redirectUri: redirectUri, userInfo: userInfo, primaryEmail: primaryEmail));
//        }


//        if (actionState == "mobile")
//            return Redirect($"{baseUrl}/?actionState=mobile&redirectUri={redirectUri}");

//        if (sameSite)
//            return Redirect($"{redirectUri}");
//        else
//            return Redirect($"{redirectUri}/login");
//    }
//}