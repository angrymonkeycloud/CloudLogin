using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using Microsoft.Azure.Cosmos;

namespace AngryMonkey.CloudLogin;
[Route("CloudLogin/User")]
[ApiController]
public class UserController : BaseController
{
    [HttpPost("SendWhatsAppCode")]
    public async Task<ActionResult> SendWhatsAppCode(string receiver, string code)
    {
        WhatsAppProviderConfiguration whatsAppProvider = Configuration.Providers.First(key => key is WhatsAppProviderConfiguration) as WhatsAppProviderConfiguration;

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
            return Problem();
        }
    }

    [HttpPost("SendEmailCode")]
    public async Task<ActionResult> SendEmailCode(string receiver, string code)
    {
        if (Configuration.EmailSendCodeRequest == null)
            return Problem("Email Code is not configured.");

        try
        {
            await Configuration.EmailSendCodeRequest.Invoke(new SendCodeValue(code, receiver));
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
    public async Task<ActionResult<User>> GetUserById(Guid id)
    {
        try
        {
            User user = await CosmosMethods.GetUserById(id);
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
    public async Task<ActionResult<User>> GetUserByDisplayName(string displayname)
    {
        try
        {
            User user = await CosmosMethods.GetUserByDisplayName(displayname);
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
    public async Task<ActionResult<User?>> CurrentUser()
    {
        try
        {
            string? userCookie = Request.Cookies["User"];

            if (userCookie == null)
                return Ok(null);

            User? user = JsonConvert.DeserializeObject<User>(userCookie);

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
    public async Task<ActionResult<bool>> IsAuthenticated()
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
