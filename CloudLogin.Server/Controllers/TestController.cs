using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AngryMonkey.CloudLogin.Controllers
{
    [Route("Test")]
    public class TestController : Controller
    {
        [HttpGet("Test")]
        public void Test()
        {
            Console.WriteLine("Test");
        }
    }
}
