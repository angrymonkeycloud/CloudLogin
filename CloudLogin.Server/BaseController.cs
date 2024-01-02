using Microsoft.AspNetCore.Mvc;

namespace AngryMonkey.CloudLogin
{
    public class BaseController(CloudLoginConfiguration configuration, CosmosMethods cosmosMethods) : Controller
    {
        internal CloudLoginConfiguration Configuration = configuration;
        internal CosmosMethods CosmosMethods = cosmosMethods;
    }
}
