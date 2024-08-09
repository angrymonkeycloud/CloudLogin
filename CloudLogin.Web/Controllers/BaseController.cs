using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin;

public class CloudLoginBaseController(CloudLoginConfiguration configuration, CosmosMethods? cosmosMethods = null) : Controller
{
    internal CloudLoginConfiguration Configuration = configuration;
    internal CosmosMethods? CosmosMethods = cosmosMethods;
}
