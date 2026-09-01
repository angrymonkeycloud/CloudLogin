using Microsoft.AspNetCore.Http;
using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Interfaces;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer(CloudGeographyClient cloudGeography, CloudLoginWebConfiguration configuration, IHttpContextAccessor httpContextAccessor, ICloudLoginStore? cloudLoginStore = null, IHttpClientFactory? httpClientFactory = null, ICloudLoginWorkspaceRegistry? workspaceRegistry = null, ICloudLoginEventPublisher? eventPublisher = null, ICloudLoginSecurityStore? securityStore = null, Core.Application.SessionService? sessionService = null)
{
    readonly CloudGeographyClient _cloudGeography = cloudGeography;
    readonly ICloudLoginStore? _cosmosMethods = cloudLoginStore;
    readonly CloudLoginWebConfiguration _configuration = configuration;
    readonly IHttpContextAccessor _accessor = httpContextAccessor;
    readonly IHttpClientFactory? _httpClientFactory = httpClientFactory;
    readonly ICloudLoginWorkspaceRegistry? _workspaceRegistry = workspaceRegistry;
    readonly ICloudLoginSecurityStore? _injectedSecurityStore = securityStore;

    /// <summary>
    /// Present only on a deployment running the V3 storage core, which is where sessions live.
    /// Null on the legacy database version, where the device list is simply unavailable.
    /// </summary>
    readonly Core.Application.SessionService? _sessionService = sessionService;

    readonly ICloudLoginEventPublisher? _eventPublisher = eventPublisher;
    private HttpRequest _request => _accessor.HttpContext!.Request;

    // Use BaseAddress from configuration as the LoginUrl, with fallback to current request
    public string LoginUrl => _configuration.BaseAddress ?? $"{_request.Scheme}://{_request.Host}";
    public string UserRoute { get; set; } = "CloudLogin/User";
    public string? RedirectUri { get; set; }
    public List<CloudLoginLink>? FooterLinks { get; set; }
    public bool UsingDatabase { get; set; } = true;
}
