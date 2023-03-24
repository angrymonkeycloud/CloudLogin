using AngryMonkey.CloudLogin.DataContract;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.Controllers;

[Route("Api/Client")]
[ApiController]
public class ClientApiController : BaseController
{
    [HttpGet("GetUserById")]
    public async Task<ActionResult<CloudUser>> GetUserById(Guid id)
    {
        try
        {
            CloudUser user = await CosmosMethods.GetUserById(id);
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }    
    [HttpGet("GetAllUsers")]
    public async Task<ActionResult<List<CloudUser>>> GetAllUsers()
    {
        try
        {
            List<CloudUser> user = await CosmosMethods.GetUsers();
            return Ok(user);
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetUsersByDisplayName")]
    public async Task<ActionResult<List<CloudUser>>> GetUsersByDisplayName(string displayname)
    {
        try
        {
            List<CloudUser> user = await CosmosMethods.GetUsersByDisplayName(displayname);
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
            CloudUser User = await CosmosMethods.GetUserByRequestId(requestId);

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