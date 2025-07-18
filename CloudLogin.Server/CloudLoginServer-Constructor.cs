using Microsoft.AspNetCore.Http;
using AngryMonkey.Cloud;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer(CloudGeographyClient cloudGeography, CloudLoginConfiguration configuration, IHttpContextAccessor httpContextAccessor, CosmosMethods? cosmosMethods = null, IHttpClientFactory? httpClientFactory = null)
{
    readonly CloudGeographyClient _cloudGeography = cloudGeography;
    readonly CosmosMethods? _cosmosMethods = cosmosMethods;
    readonly CloudLoginConfiguration _configuration = configuration;
    readonly IHttpContextAccessor _accessor = httpContextAccessor;
    readonly IHttpClientFactory? _httpClientFactory = httpClientFactory;

    private HttpRequest _request => _accessor.HttpContext!.Request;

    public string LoginUrl { get; set; } = string.Empty;
    public string UserRoute { get; set; } = "CloudLogin/User";
    public string? RedirectUri { get; set; }
    public List<Link>? FooterLinks { get; set; }
    public bool UsingDatabase { get; set; } = true;
}
