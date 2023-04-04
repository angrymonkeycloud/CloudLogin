
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin;
[Route("CloudLogin/Request")]
[ApiController]
public class RequestController : BaseController
{
    [HttpPost("CreateRequest")]
    public async Task<ActionResult> CreateRequest(Guid userId, Guid requestId)
    {
        try
        {
            CosmosMethods.CreateRequest(userId, requestId);
            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetUserByRequestId")]
    public async Task<ActionResult> GetUserByRequestId(Guid requestId)
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
}
