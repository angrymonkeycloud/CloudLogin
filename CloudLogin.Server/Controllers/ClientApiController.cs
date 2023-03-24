using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin;

[Route("Api/Client")]
[ApiController]
public class ClientApiController : BaseController
{
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
    [HttpGet("GetUserByPhoneNumber")]
    public async Task<ActionResult<User>> GetUsersByPhoneNumber(string input)
    {
        try
        {
            User? user = await CosmosMethods.GetUserByPhoneNumber(input);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }
    [HttpGet("GetUserByEmailAdress")]
    public async Task<ActionResult<User>> GetUserByEmailAdress(string input)
    {
        try
        {
            User? user = await CosmosMethods.GetUserByEmailAddress(input);
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

    [HttpGet("GetUserByRequestId")]
    public async Task<ActionResult> GetUserByRequestId(Guid requestId, int minutesToExpiry)
    {
        try
        {
            User User = await CosmosMethods.GetUserByRequestId(requestId);

            return Ok(User);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpPost("AddInput")]
    public async Task<ActionResult> AddInput(Guid userId, [FromBody] LoginInput phoneNumber)
    {
        try
        {
            await CosmosMethods.AddInput(userId, phoneNumber);

            return Ok();
        }
        catch
        {
            return Problem();
        }
    }
}