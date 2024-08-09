using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin;
[Route("CloudLogin/Request")]
[ApiController]
public class RequestController(CloudLoginConfiguration configuration, CosmosMethods? cosmosMethods = null) : CloudLoginBaseController(configuration, cosmosMethods)
{
    [HttpPost("CreateRequest")]
    public async Task<IActionResult> CreateRequest(Guid userId, Guid requestId)
    {
        if (CosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        try
        {
            if (Configuration?.Cosmos == null)
                return BadRequest();

            await CosmosMethods.CreateRequest(userId, requestId);

            return Ok();
        }
        catch
        {
            return Problem();
        }
    }

    [HttpGet("GetUserByRequestId")]
    public async Task<IActionResult> GetUserByRequestId(Guid requestId)
    {
        if (CosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        try
        {
            User? User = await CosmosMethods.GetUserByRequestId(requestId);

            return Ok(User);
        }
        catch
        {
            return Problem();
        }
    }
}
