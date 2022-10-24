using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerClientDemo.Server
{
    [Route("LoginHandler")]
    [ApiController]
    public class LoginHandlerController
    {
        [HttpGet("Result")]
        public async Task<ActionResult<string>> LoginResult(string redirectUri)
        {
            return "test";
        }
    }
}
