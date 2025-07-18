using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server;
using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin.API.Controllers;

public class CloudLoginBaseController(CloudLoginConfiguration configuration, ICloudLogin server) : Controller
{
    internal CloudLoginConfiguration Configuration = configuration;
    internal CloudLoginServer _server = (server as CloudLoginServer)!;
}
