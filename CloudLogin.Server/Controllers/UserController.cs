using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace AngryMonkey.CloudLogin;
[Route("CloudLogin/User")]
[ApiController]
public class UserController(CloudLoginConfiguration configuration, CosmosMethods? cosmosMethods = null) : CloudLoginBaseController(configuration, cosmosMethods)
{
    [HttpPost("SendWhatsAppCode")]
    public async Task<ActionResult> SendWhatsAppCode(string receiver, string code)
    {
        if (Configuration.Providers.First(key => key is WhatsAppProviderConfiguration) is not WhatsAppProviderConfiguration whatsAppProvider)
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

        try
        {
            HttpClient httpClient = new();

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                return Ok();

            return Problem();
        }
        catch (Exception e)
        {
            return Problem(e.ToString());
        }
    }

    [HttpPost("SendEmailCode")]
    public async Task<IActionResult> SendEmailCode(string receiver, string code)
    {
        if (Configuration.EmailSendCodeRequest == null && Configuration.EmailConfiguration == null)
            return Problem("Email is not configured.");

        try
        {
            if (Configuration.EmailSendCodeRequest != null)
                await Configuration.EmailSendCodeRequest.Invoke(new SendCodeValue(code, receiver));

            if (Configuration.EmailConfiguration != null)
            {
                string subject = Configuration.EmailConfiguration.DefaultSubject;
                string body = Configuration.EmailConfiguration.DefaultBody.Replace(CloudLoginEmailConfiguration.VerificationCodePlaceHolder, code);

                await Configuration.EmailConfiguration.EmailService.SendEmail(subject, body, [receiver]);
            }

            return Ok();
        }
        catch (Exception e)
        {
            return Problem(e.Message);
        }
    }

    [HttpPost("Update")]
    public async Task<ActionResult> Update([FromBody] User user)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            await CosmosMethods.Update(user);

            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("Create")]
    public async Task<ActionResult> Create([FromBody] User user)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            await CosmosMethods.Create(user);

            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("AddUserInput")]
    public async Task<ActionResult> AddInput(Guid userId, [FromBody] LoginInput Input)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            await CosmosMethods.AddInput(userId, Input);
            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpDelete("Delete")]
    public async Task<ActionResult> Delete(Guid userId)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            await CosmosMethods.DeleteUser(userId);
            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("All")]
    public async Task<ActionResult<List<User>>> All()
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            List<User> users = await CosmosMethods.GetUsers();
            return Ok(users);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetAllUsers")]
    public async Task<ActionResult<List<User>>> GetAllUsers()
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            List<User> user = await CosmosMethods.GetUsers();

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserById")]
    public async Task<ActionResult<User?>> GetUserById(Guid id)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            User? user = await CosmosMethods.GetUserById(id);

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUsersByDisplayName")]
    public async Task<ActionResult<List<User>>> GetUsersByDisplayName(string displayname)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            List<User> user = await CosmosMethods.GetUsersByDisplayName(displayname);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByDisplayName")]
    public async Task<ActionResult<User?>> GetUserByDisplayName(string displayname)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            User? user = await CosmosMethods.GetUserByDisplayName(displayname);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByInput")]
    public async Task<ActionResult<User>> GetUserByInput(string input)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            User? user = await CosmosMethods.GetUserByInput(input);

            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByEmailAdress")]
    public async Task<ActionResult<User>> GetUserByEmailAdress(string email)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            User? user = await CosmosMethods.GetUserByEmailAddress(email);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByPhoneNumber")]
    public async Task<ActionResult<User>> GetUsersByPhoneNumber(string number)
    {
        if (CosmosMethods == null)
            throw new NullReferenceException(nameof(CosmosMethods));

        try
        {
            User? user = await CosmosMethods.GetUserByPhoneNumber(number);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
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
}
