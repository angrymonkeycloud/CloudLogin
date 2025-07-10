using System.Text.Json;
using AngryMonkey.CloudLogin;

namespace AngryMonkey.CloudLogin.Server.Tests;

public static class CloudLoginSerialization
{
    public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

// Mock interfaces and classes for testing
public interface IEmailService
{
    Task SendEmail(string subject, string body, List<string> recipients);
}

public static class CloudLoginShared
{
    public static string RedirectString(string controller, string action, 
        string? keepMeSignedIn = null, string? redirectUri = null, 
        string? sameSite = null, string? actionState = null, 
        string? primaryEmail = null, string? userInfo = null,
        string? inputValue = null)
    {
        List<string> parameters = [];
        
        if (!string.IsNullOrEmpty(keepMeSignedIn))
            parameters.Add($"keepMeSignedIn={keepMeSignedIn}");
        if (!string.IsNullOrEmpty(redirectUri))
            parameters.Add($"redirectUri={System.Web.HttpUtility.UrlEncode(redirectUri)}");
        if (!string.IsNullOrEmpty(sameSite))
            parameters.Add($"sameSite={sameSite}");
        if (!string.IsNullOrEmpty(actionState))
            parameters.Add($"actionState={actionState}");
        if (!string.IsNullOrEmpty(primaryEmail))
            parameters.Add($"primaryEmail={System.Web.HttpUtility.UrlEncode(primaryEmail)}");
        if (!string.IsNullOrEmpty(userInfo))
            parameters.Add($"userInfo={System.Web.HttpUtility.UrlEncode(userInfo)}");
        if (!string.IsNullOrEmpty(inputValue))
            parameters.Add($"input={System.Web.HttpUtility.UrlEncode(inputValue)}");

        string queryString = parameters.Count > 0 ? "?" + string.Join("&", parameters) : "";
        
        // Handle empty controller/action case
        if (string.IsNullOrEmpty(controller) && string.IsNullOrEmpty(action))
            return "/" + queryString;
            
        return $"/{controller}/{action}{queryString}";
    }
}