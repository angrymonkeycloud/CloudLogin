using AngryMonkey.Cloud.Login.DataContract;
using CoverboxApp.Main.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace CoverboxApp.Main.Controllers
{
    public class HomeController : Controller
    {



        public IActionResult Index()
        {
            return View();
        }

        [HttpPost("Login")]
        public IActionResult Login([FromBody] CloudUser User)
        {
            return View();
        }

    }
}