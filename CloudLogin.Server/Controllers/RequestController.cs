﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin;
[Route("CloudLogin/Request")]
[ApiController]
public class RequestController(CloudLoginConfiguration configuration, CosmosMethods? cosmosMethods = null) : BaseController(configuration, cosmosMethods)
{
    [HttpPost("CreateRequest")]
    public async Task<IResult> CreateRequest(Guid userId, Guid requestId)
    {
        if (CosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        try
        {
            if (Configuration?.Cosmos == null)
                return Results.BadRequest();

            await CosmosMethods.CreateRequest(userId, requestId);

            return Results.Ok();
        }
        catch
        {
            return Results.Problem();
        }
    }

    [HttpGet("GetUserByRequestId")]
    public async Task<IResult> GetUserByRequestId(Guid requestId)
    {
        if (CosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        try
        {
            User? User = await CosmosMethods.GetUserByRequestId(requestId);

            return Results.Ok(User);
        }
        catch
        {
            return Results.Problem();
        }
    }
}
