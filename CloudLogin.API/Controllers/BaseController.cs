using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin;

public class CloudLoginBaseController(CloudLoginConfiguration configuration, CloudLoginServer server) : Controller
{
    internal CloudLoginConfiguration Configuration = configuration;
    internal CloudLoginServer _server = server;
}
