using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.Cloud.Login.Controllers;
[Route("Api/Client")]
[ApiController]
public class LibraryController : BaseController
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
            CloudUser User = await CosmosMethods.GetRequest(requestId, minutesToExpiry);

            return Ok(User);
        }
        catch
        {
            return Problem();
        }
    }
}