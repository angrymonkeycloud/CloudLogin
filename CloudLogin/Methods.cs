using System.Web;

namespace AngryMonkey.CloudLogin;
public class Methods
{
    public string RedirectString(string controller, string function, string? redirectUri = null, string? keepMeSignedIn = null, string? sameSite = null, string? actionState = null, string? primaryEmail = null, string? userInfo = null, string? inputValue = null)
    {
        var redirectParams = new List<string>();

        if (redirectUri != null)
            redirectParams.Add($"redirecturi={HttpUtility.UrlEncode(redirectUri)}");

        if (keepMeSignedIn != null)
            redirectParams.Add($"keepMeSignedIn={HttpUtility.UrlEncode(keepMeSignedIn)}");

        if (sameSite != null)
            redirectParams.Add($"samesite={HttpUtility.UrlEncode(sameSite)}");

        if (actionState != null)
            redirectParams.Add($"actionState={HttpUtility.UrlEncode(actionState)}");

        if (primaryEmail != null)
            redirectParams.Add($"primaryEmail={HttpUtility.UrlEncode(primaryEmail)}");

        if (userInfo != null)
            redirectParams.Add($"userInfo={HttpUtility.UrlEncode(userInfo)}");

        if (inputValue != null)
            redirectParams.Add($"input={HttpUtility.UrlEncode(inputValue)}");
        

        string redirectString = $"/{controller}/{function}/?{string.Join("&", redirectParams)}";
        return redirectString;
    }
}