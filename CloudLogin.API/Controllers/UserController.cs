using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

[Route("CloudLogin/User")]
[ApiController]
public class UserController(CloudLoginConfiguration configuration, ICloudLogin server) : CloudLoginBaseController(configuration, server)
{
    [HttpPost("SendWhatsAppCode")]
    public async Task<ActionResult> SendWhatsAppCode(string receiver, string code)
    {
        try
        {
            await _server.SendWhatsAppCode(receiver, code);

            return Ok();
        }
        catch (Exception e)
        {
            return Problem(e.ToString());
        }
    }

    [HttpPost("SendEmailCode")]
    public async Task<IActionResult> SendEmailCode(string receiver, string code)
    {
        try
        {
            await _server.SendEmailCode(receiver, code);

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
            await _server.UpdateUser(user);

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
            await _server.CreateUser(user);

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
            await _server.AddUserInput(userId, Input);
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
            await _server.DeleteUser(userId);
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
            List<User> users = await _server.GetAllUsers();
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
            List<User> user = await _server.GetAllUsers();

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
        try
        {
            User? user = await _server.GetUserById(id);

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
            List<User> user = await _server.GetUsersByDisplayName(displayname);
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
        try
        {
            User? user = await _server.GetUserByDisplayName(displayname);
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
            User? user = await _server.GetUserByInput(input);

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
            User? user = await _server.GetUserByEmailAddress(email);

            if (user == null)
                return NotFound();

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
            User? user = await _server.GetUserByPhoneNumber(number);

            if (user == null)
                return NotFound();

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
            User? user = await _server.CurrentUser();

            if (user == null)
                return NotFound();

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
            bool isAuthenticated = await _server.IsAuthenticated();

            return Ok(isAuthenticated);
        }
        catch
        {
            return Problem();
        }
    }
}
