using Microsoft.AspNetCore.Http;
using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Interfaces;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer(CloudGeographyClient cloudGeography, CloudLoginWebConfiguration configuration, IHttpContextAccessor httpContextAccessor, ICloudLoginStore? cloudLoginStore = null, IHttpClientFactory? httpClientFactory = null, ICloudLoginWorkspaceRegistry? workspaceRegistry = null, ICloudLoginSubscriptionRegistry? subscriptionRegistry = null, ICloudLoginAccountStore? accountStore = null, ICloudLoginEventPublisher? eventPublisher = null)
{
    readonly CloudGeographyClient _cloudGeography = cloudGeography;
    readonly ICloudLoginStore? _cosmosMethods = cloudLoginStore;
    readonly CloudLoginWebConfiguration _configuration = configuration;
    readonly IHttpContextAccessor _accessor = httpContextAccessor;
    readonly IHttpClientFactory? _httpClientFactory = httpClientFactory;
    readonly ICloudLoginWorkspaceRegistry? _workspaceRegistry = workspaceRegistry;
    readonly ICloudLoginSubscriptionRegistry? _subscriptionRegistry = subscriptionRegistry;
    readonly ICloudLoginAccountStore? _accountStore = accountStore;

    readonly ICloudLoginEventPublisher? _eventPublisher = eventPublisher;
    private HttpRequest _request => _accessor.HttpContext!.Request;

    // Use BaseAddress from configuration as the LoginUrl, with fallback to current request
    public string LoginUrl => _configuration.BaseAddress ?? $"{_request.Scheme}://{_request.Host}";
    public string UserRoute { get; set; } = "CloudLogin/User";
    public string? RedirectUri { get; set; }
    public List<CloudLoginLink>? FooterLinks { get; set; }
    public bool UsingDatabase { get; set; } = true;
}
