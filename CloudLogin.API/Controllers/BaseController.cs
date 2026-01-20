using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

public class CloudLoginBaseController(CloudLoginWebConfiguration configuration, ICloudLogin server) : Controller
{
    internal CloudLoginWebConfiguration Configuration = configuration;
    internal CloudLoginServer _server = (server as CloudLoginServer)!;
}
