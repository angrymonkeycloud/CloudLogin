using AngryMonkey.Cloud.Login.DataContract;
using CoverboxApp.Main.Models;
using LoginRequestLibrary;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace CoverboxApp.Main.Controllers
{
    [Route("")]
    public class HomeController : Controller
    {
        [Route("")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Route("login")]
        public async Task<IActionResult> Login(Guid requestId)
        {
            Requests request = new();


            CloudUser? user = await  request.GetRequestFromDB(requestId);


            return View(user);
        }

    }
}