using System.Web;
using System.Security.Claims;
using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using AngryMonkey.Cloud;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    public CloudLoginServer(CloudGeographyClient cloudGeography, CloudLoginConfiguration configuration, IHttpContextAccessor httpContextAccessor, CosmosMethods? cosmosMethods = null)
    {
        _cloudGeography = cloudGeography;
        _cosmosMethods = cosmosMethods;
        _configuration = configuration;
        _accessor = httpContextAccessor;
    }

    readonly CloudGeographyClient _cloudGeography;
    readonly CosmosMethods? _cosmosMethods;
    readonly CloudLoginConfiguration _configuration;
    readonly IHttpContextAccessor _accessor;

    private HttpRequest _request => _accessor.HttpContext!.Request;

    public string LoginUrl { get; set; } = string.Empty;
    public string UserRoute { get; set; } = "CloudLogin/User";
    public string? RedirectUri { get; set; }
    public List<Link>? FooterLinks { get; set; }
    public bool UsingDatabase { get; set; } = true;
}
