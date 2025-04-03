using System.Web;
using System.Security.Claims;
using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer : ICloudLogin
{
    public InputFormat GetInputFormat(string input)
    {
        if (string.IsNullOrEmpty(input))
            return InputFormat.Other;

        if (IsInputValidEmailAddress(input))
            return InputFormat.EmailAddress;

        if (IsInputValidPhoneNumber(input))
            return InputFormat.PhoneNumber;

        return InputFormat.Other;
    }

    public static bool IsInputValidEmailAddress(string input) => Regex.IsMatch(input, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
    public bool IsInputValidPhoneNumber(string input) => _cloudGeography.PhoneNumbers.IsValidPhoneNumber(input);

    public IActionResult Login(HttpRequest request, string? returnUrl)
    {
        string baseUrl = $"{request.Scheme}://{request.Host}";

        string separator = LoginUrl.Contains('?') ? "&" : "?";

        if (string.IsNullOrEmpty(returnUrl))
            returnUrl = baseUrl;

        string redirectUri = $"{baseUrl}/Account/LoginResult?ReturnUrl={HttpUtility.UrlEncode(returnUrl)}";

        return new RedirectResult($"{LoginUrl}{separator}redirectUri={HttpUtility.UrlEncode(redirectUri)}&actionState=login");
    }

    public async Task<User?> CurrentUser()
    {
        string? userCookie = _request.Cookies["CloudLogin"];

        if (userCookie == null)
            return null;

        ClaimsIdentity userIdentity = _request.HttpContext.User.Identities.First();

        string? loginIdentity = userIdentity.FindFirst(ClaimTypes.UserData)?.Value;

        if (string.IsNullOrEmpty(loginIdentity))
            return null;

        return JsonSerializer.Deserialize<User?>(loginIdentity, CloudLoginSerialization.Options);
    }

    public async Task<bool> IsAuthenticated()
    {
        return _request.Cookies["CloudLogin"] != null;
    }

    public async Task<List<User>> GetAllUsers()
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        return await _cosmosMethods.GetUsers() ?? [];
    }

    public async Task<User?> GetUserById(Guid userId)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        return await _cosmosMethods.GetUserById(userId);
    }

    public async Task<List<User>> GetUsersByDisplayName(string displayName)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        return await _cosmosMethods.GetUsersByDisplayName(displayName);
    }

    public async Task<User?> GetUserByDisplayName(string displayName)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        return await _cosmosMethods.GetUserByDisplayName(displayName);
    }

    public async Task<User?> GetUserByInput(string input)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        return await _cosmosMethods.GetUserByInput(input);
    }

    public async Task<User?> GetUserByEmailAddress(string email)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        return await _cosmosMethods.GetUserByEmailAddress(email);
    }

    public async Task<User?> GetUserByPhoneNumber(string number)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        return await _cosmosMethods.GetUserByPhoneNumber(number);
    }

    public async Task<User?> GetUserByRequestId(Guid requestId)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        return await _cosmosMethods.GetUserByRequestId(requestId);
    }

    public async Task<Guid> CreateLoginRequest(Guid userId, Guid? requestId = null)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        LoginRequest request = await _cosmosMethods.CreateRequest(userId, requestId);

        return request.ID;
    }

    public async Task SendWhatsAppCode(string receiver, string code)
    {
        if (_configuration.Providers.First(key => key is WhatsAppProviderConfiguration) is not WhatsAppProviderConfiguration whatsAppProvider)
            throw new ArgumentNullException(nameof(whatsAppProvider));

        string serialize = "{\"messaging_product\": \"whatsapp\",\"recipient_type\": \"individual\",\"to\": \"" + receiver.Replace("+", "") + "\",\"type\": \"template\",\"template\": {\"name\": \"" + whatsAppProvider.Template + "\",\"language\": {\"code\": \"" + whatsAppProvider.Language + "\"},\"components\": [{\"type\": \"body\",\"parameters\": [{\"type\": \"text\",\"text\": \"" + code + "\"}]}]}}";

        using HttpRequestMessage request = new()
        {
            Method = new HttpMethod("POST"),
            RequestUri = new(whatsAppProvider.RequestUri),
            Content = new StringContent(serialize),
        };

        request.Headers.Add("Authorization", whatsAppProvider.Authorization);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpClient httpClient = new();

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            throw new Exception("Failed to send WhatsApp code.");
    }

    public async Task SendEmailCode(string receiver, string code)
    {
        if (_configuration.EmailSendCodeRequest == null && _configuration.EmailConfiguration == null)
            throw new Exception("Email is not configured.");

        if (_configuration.EmailSendCodeRequest != null)
            await _configuration.EmailSendCodeRequest.Invoke(new SendCodeValue(code, receiver));

        if (_configuration.EmailConfiguration != null)
        {
            string subject = _configuration.EmailConfiguration.DefaultSubject;
            string body = _configuration.EmailConfiguration.DefaultBody.Replace(CloudLoginEmailConfiguration.VerificationCodePlaceHolder, code);

            await _configuration.EmailConfiguration.EmailService.SendEmail(subject, body, [receiver]);
        }
    }

    public async Task UpdateUser(User user)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        await _cosmosMethods.Update(user);
    }

    public async Task CreateUser(User user)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        await _cosmosMethods.Create(user);
    }

    public async Task DeleteUser(Guid userId)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        await _cosmosMethods.DeleteUser(userId);
    }

    public async Task AddUserInput(Guid userId, LoginInput input)
    {
        if (_cosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        await _cosmosMethods.AddInput(userId, input);
    }

    public async Task<bool> AutomaticLogin()
    {
        throw new NotImplementedException();
    }

    public async Task<List<ProviderDefinition>> GetProviders()
    {
        // generate this method body
        if (_configuration.Providers == null)
            throw new NullReferenceException(nameof(_configuration.Providers));

        return [.. _configuration.Providers.Select(key => new ProviderDefinition(key.Code, key.HandleUpdateOnly, key.Label))];
    }
}
