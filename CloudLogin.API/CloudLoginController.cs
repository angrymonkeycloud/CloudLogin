using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API;

[Route("Account")]
[ApiController]
public class CloudLoginController(CloudLoginServer cloudLoginServer) : Controller
{
    private readonly CloudLoginServer _cloudLoginServer = cloudLoginServer;

    [Route("Login")]
    public IActionResult Login(string? ReturnUrl)
    {
        return _cloudLoginServer.Login(Request, ReturnUrl);
    }

    [Route("LoginResult")]
    public async Task<IActionResult> LoginResult(Guid requestId, string? currentUser, string? ReturnUrl, bool KeepMeSignedIn)
    {
        return await _cloudLoginServer.LoginResult(Request, Response, requestId, currentUser, ReturnUrl, KeepMeSignedIn);
    }

    [Route("Logout")]
    public async Task<IActionResult> Logout()
    {
        return new RedirectResult(await _cloudLoginServer.Logout(Request, Response));
    }

    [Route("ChangePrimary")]
    public IActionResult ChangePrimary()
    {
        return new RedirectResult(_cloudLoginServer.ChangePrimary());
    }

    [Route("AddInput")]
    public IActionResult AddInput()
    {
        return new RedirectResult(_cloudLoginServer.GetAddInputUrl());
    }

    [Route("Update")]
    public IActionResult Update()
    {
        return new RedirectResult(_cloudLoginServer.GetUpdateUrl());
    }

    [HttpGet("CurrentUser")]
    public async Task<ActionResult<User?>> CurrentUser()
    {
        User? user = await _cloudLoginServer.CurrentUser();

        if (user == null)
            return new NotFoundResult();

        return new OkObjectResult(user);
    }

    [HttpGet("IsAuthenticated")]
    public async Task<ActionResult<bool>> IsAuthenticated()
    {
        return new OkObjectResult(await _cloudLoginServer.IsAuthenticated());
    }

    [HttpGet("AutomaticLogin")]
    public ActionResult<bool> AutomaticLogin()
    {
        return _cloudLoginServer.AutomaticLogin(Request);
    }
}
