﻿using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Web;

namespace AngryMonkey.CloudLogin;
[Route("CloudLogin/Actions")]
[ApiController]
public class ActionsController(CloudLoginConfiguration configuration, CloudLoginServer server) : CloudLoginBaseController(configuration, server)
{
    [HttpGet("AddInput")]
    public async Task<ActionResult> AddInput(string redirectUrl, string userInfo, string primaryEmail)
    {
        string result = await _server.AddInput(redirectUrl, userInfo, primaryEmail);

        return Redirect(result);
    }

    [HttpGet("Update")]
    public async Task<ActionResult> Update(string userInfo, string domainName)
    {
        string result = await _server.Update(userInfo, domainName);

        return Redirect(result);
    }

    [HttpGet("SetPrimary")]
    public async Task<ActionResult> SetPrimary(string input, string domainName)
    {
        string result = await _server.SetPrimary(input, domainName);

        return Redirect(result);
    }
}
