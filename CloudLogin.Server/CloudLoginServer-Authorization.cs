using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.Twitter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Web;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    /// <summary>
    /// Whether the sign-in profile in play on this request permits <paramref name="method"/> to
    /// complete a sign-in.
    /// <para>
    /// Every entry path has to ask, not just the provider redirect: a profile that lists only
    /// <c>Qr</c> would be worth nothing if the password form still signed people in, since the
    /// point of a restricted profile is that the other methods are unavailable on that device.
    /// Provider sign-in checks the profile it sealed into the authentication ticket at challenge
    /// time; the direct methods have no ticket to carry one, so they resolve the request's
    /// profile the same way the challenge did — and resolution already falls back to the default
    /// profile for an unknown or unauthorized name, so a forged parameter can only narrow.
    /// </para>
    /// </summary>
    private bool SignInProfileAllows(string method)
    {
        Core.Application.SignInProfileService? profileService =
            _accessor.HttpContext?.RequestServices.GetService<Core.Application.SignInProfileService>();

        if (profileService is null)
            return true;

        string? requestedProfile = ProfileParameter("profile");
        string? client = ProfileParameter("client");

        Core.Application.SignInProfileSelection selection = profileService.Resolve(requestedProfile, client);

        return Core.Application.SignInProfileService.AllowsMethod(selection.Profile, method);
    }

    /// <summary>
    /// Reads a profile parameter from wherever this request carries it. Password sign-in posts a
    /// form, the login page navigates with a query string, and both must reach the same profile.
    /// </summary>
    private string? ProfileParameter(string name)
    {
        string? fromQuery = _accessor.HttpContext?.Request.Query[name].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(fromQuery))
            return fromQuery;

        HttpRequest? request = _accessor.HttpContext?.Request;

        return request?.HasFormContentType == true ? request.Form[name].FirstOrDefault() : null;
    }

    public async Task<string> CompleteLoginRedirect(string? referer = null, bool isMobileApp = false)
    {
        if (!IsAllowedRedirect(referer))
            throw new ArgumentException("The requested return URL is not allowed.", nameof(referer));

        if (_accessor.HttpContext?.User.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("A signed-in user is required to complete login.");

        CloudUser? currentUser = await CurrentUser();
        if (currentUser is null || currentUser.ID == Guid.Empty || currentUser.IsLocked)
            throw new UnauthorizedAccessException("A signed-in user is required to complete login.");

        return await BuildCompletedLoginRedirect(currentUser.ID, referer, isMobileApp);
    }

    /// <summary>
    /// Safely completes test sign-in requests emitted by CloudLogin clients that
    /// predate the POST-based TestSignIn flow. The caller-provided user payload is
    /// never trusted; the supplied identifier is reloaded and validated by
    /// <see cref="TestLogin(Guid, bool)"/>.
    /// </summary>
    public async Task<IActionResult> CompleteLegacyTestLogin(
        Guid userId,
        bool keepMeSignedIn,
        string? referer = null,
        bool isMobileApp = false)
    {
        if (!IsAllowedRedirect(referer))
            return new BadRequestObjectResult("The requested return URL is not allowed.");

        if (!await TestLogin(userId, keepMeSignedIn))
            return new UnauthorizedResult();

        return new RedirectResult(await BuildCompletedLoginRedirect(userId, referer, isMobileApp));
    }

    private async Task<string> BuildCompletedLoginRedirect(
        Guid userId,
        string? referer,
        bool isMobileApp)
    {
        string target = string.IsNullOrWhiteSpace(referer) || referer == "/" ? "/Account" : referer;
        bool isExternal = !IsRelativePath(target) &&
                          Uri.TryCreate(target, UriKind.Absolute, out _) &&
                          !CloudLoginShared.IsSameOrigin(target, LoginUrl);

        if (isExternal)
        {
            Guid requestId = await CreateLoginRequest(userId);
            target = CloudLoginShared.AppendQueryParameter(target, "requestId", requestId.ToString());
        }

        if (isMobileApp)
            target = CloudLoginShared.AppendQueryParameter(target, "isMobileApp", "true");

        return target;
    }

    public async Task<IActionResult> Login(string identity, bool keepMeSignedIn, bool sameSite, string primaryEmail = "", string? input = null, string? referer = null, bool isMobileApp = false)
    {
        if (!IsAllowedRedirect(referer))
            return new BadRequestObjectResult("The requested return URL is not allowed.");

        // The OAuth provider redirect URI - this is fixed and configured in the OAuth provider
        // It should always point back to our CloudLogin service (NOT the external website)
        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host}";
        string oauthRedirectUri = $"{baseUrl}/CloudLogin/Result";

        // Create authentication properties with the OAuth redirect URI
        AuthenticationProperties globalProperties = new()
        {
            RedirectUri = oauthRedirectUri // This is for OAuth providers
        };

        Core.Application.SignInProfileService? profileService =
            _accessor.HttpContext?.RequestServices.GetService<Core.Application.SignInProfileService>();
        string? profileClient = _request.Query["client"].FirstOrDefault();
        if (profileService is not null)
        {
            Core.Application.SignInProfileSelection selection =
                profileService.Resolve(_request.Query["profile"].FirstOrDefault(), profileClient);
            if (!Core.Application.SignInProfileService.AllowsMethod(selection.Profile, identity))
                return new NotFoundResult();

            globalProperties.Items["cloudlogin:profile"] =
                profileService.Bind(selection, profileClient);
            if (profileClient is not null)
                globalProperties.Items["cloudlogin:profile_client"] = profileClient;
        }

        // Store the external website's URL in authentication properties for later use
        if (!string.IsNullOrEmpty(referer))
            globalProperties.Items["referer"] = referer;

        if (!string.IsNullOrEmpty(input))
            globalProperties.SetParameter("login_hint", input);

        if (isMobileApp)
            globalProperties.Items["isMobileApp"] = "true";

        if (keepMeSignedIn)
            globalProperties.Items["keepMeSignedIn"] = keepMeSignedIn.ToString();

        if (sameSite)
            globalProperties.Items["sameSite"] = "true";

        if (!string.IsNullOrEmpty(primaryEmail))
            globalProperties.Items["primaryEmail"] = primaryEmail;

        // If user is already authenticated, redirect appropriately
        if (_accessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            // If no external referer, go to account page
            if (string.IsNullOrEmpty(referer) || referer == "/" || referer == baseUrl || referer == $"{baseUrl}/")
                return new RedirectResult($"{baseUrl}/Account");

            CloudUser? currentUser = await CurrentUser();

            if (currentUser is not null && currentUser.ID != Guid.Empty && !currentUser.IsLocked)
            {
                Guid requestId = await CreateLoginRequest(currentUser.ID);
                referer = AppendQuery(referer, "requestId", requestId.ToString());
                return new RedirectResult(referer);
            }

            await _accessor.HttpContext.SignOutAsync();
        }

        return identity.Trim().ToLower() switch
        {
            "microsoft" => new ChallengeResult(MicrosoftAccountDefaults.AuthenticationScheme, globalProperties),
            "google" => new ChallengeResult(GoogleDefaults.AuthenticationScheme, globalProperties),
            "facebook" => new ChallengeResult(FacebookDefaults.AuthenticationScheme, globalProperties),
            "twitter" => new ChallengeResult(TwitterDefaults.AuthenticationScheme, globalProperties),
            _ => new NotFoundResult(),
        };
    }

    public async Task<IActionResult> CustomLogin(Guid userId, bool keepMeSignedIn, string? referer = null, bool sameSite = false, bool isMobileApp = false)
    {
        if (!_configuration.EnableLegacyClientManagedLogin)
            return new NotFoundResult();

        if (!IsAllowedRedirect(referer))
            return new BadRequestObjectResult("The requested return URL is not allowed.");

        // The verification-code flow completes here, so the profile has to be honoured here too.
        if (!SignInProfileAllows("Code"))
            return new NotFoundResult();

        CloudUser? user = await GetUserById(userId);
        if (user is null || user.IsTest || user.IsLocked)
            return new UnauthorizedResult();

        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host}";

        referer ??= string.Empty;
        sameSite = string.IsNullOrWhiteSpace(referer) ||
                   !Uri.TryCreate(referer, UriKind.Absolute, out _) ||
                   CloudLoginShared.IsSameOrigin(referer, baseUrl);

        if (sameSite)
        {
            referer = referer.Replace($"{baseUrl}/", "");
            referer = referer.Replace($"/login", "");
        }

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = keepMeSignedIn ? DateTimeOffset.UtcNow.Add(_configuration.LoginDuration) : null,
            IsPersistent = keepMeSignedIn,
            RedirectUri = referer
        };

        ClaimsPrincipal claimsPrincipal = await CloudLoginAuthenticationClaims.CreateAsync(
            user, "CloudLogin", _cosmosMethods);

        await _accessor.HttpContext!.SignInAsync(claimsPrincipal, properties);

        if (string.IsNullOrEmpty(referer))
            referer = "/";

        if (!sameSite)
        {
            Guid requestId = await CreateLoginRequest(user.ID);
            referer = CloudLoginShared.AppendQueryParameter(referer, "requestId", requestId.ToString());
            referer = CloudLoginShared.AppendQueryParameter(referer, "keepMeSignedIn", keepMeSignedIn.ToString().ToLowerInvariant());
        }

        if (isMobileApp)
            referer = CloudLoginShared.AppendQueryParameter(referer, "isMobileApp", "true");

        return new RedirectResult(referer);
    }

    /// <summary>
    /// The provider entry recorded on a new account's input: which external provider vouched for
    /// it, and that provider's own subject identifier. Both come from the ticket the provider's
    /// handler built — the identity's authentication type is the provider name (see
    /// <c>ProviderConfigurationService</c>), and the name identifier is its subject.
    /// </summary>
    private static List<CloudLoginProvider> ExternalProviderEntry(ClaimsIdentity identity)
    {
        string? code = identity.AuthenticationType;

        if (string.IsNullOrWhiteSpace(code))
            return [];

        return
        [
            new CloudLoginProvider
            {
                Code = code,
                Identifier = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
            }
        ];
    }

    public async Task<IActionResult> LoginResult(bool keepMeSignedIn, bool sameSite, bool isMobileApp = false)
    {
        if (_cosmosMethods == null)
            throw new ArgumentNullException(nameof(CosmosMethods));

        ClaimsIdentity userIdentity = _request.HttpContext.User.Identities.First();
        string emailaddress = userIdentity.FindFirst(ClaimTypes.Email)?.Value!;

        CloudUser? existingUser = _configuration.Cosmos != null && !string.IsNullOrWhiteSpace(emailaddress)
            ? await _cosmosMethods.GetUserByInput(emailaddress)
            : null;

        CloudUser user = existingUser ?? new();

        string baseUrl = $"http{(_request.IsHttps ? "s" : string.Empty)}://{_request.Host}";

        // Try to get the referer (external website) from authentication properties
        string? referer = null;

        // Method 1: Try to get from authentication result
        AuthenticateResult authenticateResult = await _request.HttpContext.AuthenticateAsync();
        if (authenticateResult.Succeeded && authenticateResult.Properties?.Items != null)
        {
            if (authenticateResult.Properties.Items.TryGetValue("referer", out string? storedReferer))
                referer = storedReferer;
        }

        // Method 2: Try from HttpContext features if Method 1 failed
        if (string.IsNullOrEmpty(referer))
        {
            AuthenticateResult? authResult = _request.HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;

            if (authResult?.Properties?.Items != null && authResult.Properties.Items.TryGetValue("referer", out string? featureReferer))
                referer = featureReferer;
        }

        // Method 3: Check query parameters as fallback (legacy support)
        if (string.IsNullOrEmpty(referer))
            referer = _request.Query["referer"].FirstOrDefault() ?? _request.Query["referredUrl"].FirstOrDefault() ?? _request.Query["redirectUri"].FirstOrDefault();

        // Get other stored values
        string? storedIsMobileApp = null;
        string? storedKeepMeSignedIn = null;
        string? storedSameSite = null;

        if (authenticateResult.Succeeded && authenticateResult.Properties?.Items != null)
        {
            authenticateResult.Properties.Items.TryGetValue("isMobileApp", out storedIsMobileApp);
            authenticateResult.Properties.Items.TryGetValue("keepMeSignedIn", out storedKeepMeSignedIn);
            authenticateResult.Properties.Items.TryGetValue("sameSite", out storedSameSite);
        }

        // Use stored values if available, otherwise use parameters
        bool finalIsMobileApp = bool.TryParse(storedIsMobileApp, out bool parsedIsMobileApp) ? parsedIsMobileApp : isMobileApp;
        bool finalKeepMeSignedIn = bool.TryParse(storedKeepMeSignedIn, out bool parsedKeepMeSignedIn) ? parsedKeepMeSignedIn : keepMeSignedIn;
        bool finalSameSite = bool.TryParse(storedSameSite, out bool parsedSameSite) ? parsedSameSite : sameSite;


        if (!Uri.IsWellFormedUriString(referer, UriKind.Absolute))
            referer = HttpUtility.UrlDecode(referer);

        if (!IsAllowedRedirect(referer))
            return new BadRequestObjectResult("The requested return URL is not allowed.");

        AuthenticationProperties properties = new()
        {
            ExpiresUtc = finalKeepMeSignedIn ? DateTimeOffset.UtcNow.Add(_configuration.LoginDuration) : null,
            IsPersistent = finalKeepMeSignedIn
        };

        string? firstName = user.FirstName ??= userIdentity.FindFirst(ClaimTypes.GivenName)?.Value;
        string? lastName = user.LastName ??= userIdentity.FindFirst(ClaimTypes.Surname)?.Value;
        string? displayName = user.DisplayName ??= $"{firstName} {lastName}";

        if (existingUser is null)
        {
            // First sign-in with this provider identity: the account has to be made here, because
            // an external provider is the only step this flow has - there is no registration form
            // behind it to collect anything else.
            //
            // Persisting it is the part that matters. An unsaved user keeps ID = Guid.Empty, which
            // silently skips the CreateLoginRequest below, so the relying party is handed a request
            // id that resolves to nothing and reports the person as not found. That is invisible on
            // any database that already holds the account, and breaks every sign-in on one that
            // does not - a freshly provisioned environment, most of all.
            if (_configuration.Cosmos != null && string.IsNullOrWhiteSpace(emailaddress))
            {
                // Nothing to key an account on. Saying so is better than signing in a user that was
                // never stored, which fails later and somewhere else.
                return new BadRequestObjectResult(
                    "The sign-in provider did not return an email address, so no account could be created.");
            }

            user = new()
            {
                DisplayName = displayName,
                FirstName = firstName,
                LastName = lastName,
                ID = Guid.NewGuid(),
                CreatedOn = DateTimeOffset.UtcNow,
                LastSignedIn = DateTimeOffset.UtcNow,
                Inputs =
                [
                    new()
                    {
                        Format = CloudLoginInputFormat.EmailAddress,
                        Input = emailaddress?.Trim().ToLowerInvariant() ?? string.Empty,
                        IsPrimary = true,
                        Providers = ExternalProviderEntry(userIdentity)
                    }
                ]
            };

            if (_configuration.Cosmos != null)
                await CreateUser(user);
        }

        if (user == null)
            return new RedirectResult(referer ?? "/");

        if (user.IsLocked)
        {
            await _request.HttpContext.SignOutAsync();
            return new ForbidResult();
        }

        // Create request ID for the external website
        Guid requestId = Guid.NewGuid();
        if (_configuration.Cosmos != null && user.ID != Guid.Empty)
            requestId = await CreateLoginRequest(user.ID);

        ClaimsPrincipal claimsPrincipal = await CloudLoginAuthenticationClaims.CreateAsync(
            user, "CloudLogin", _cosmosMethods);

        await _request.HttpContext.SignInAsync(claimsPrincipal, properties);


        // If no valid external referer, redirect to account page directly without request ID
        // Consider "/" or base URL as "no external referer"
        if (string.IsNullOrEmpty(referer) || referer == "/" || referer == baseUrl || referer == $"{baseUrl}/")
        {
            string accountUrl = $"{baseUrl}/Account";
            if (finalIsMobileApp)
            {
                string separator = accountUrl.Contains('?') ? "&" : "?";
                accountUrl = $"{accountUrl}{separator}isMobileApp=true";
            }
            return new RedirectResult(accountUrl);
        }

        // Build final redirect URL with user data for the external website
        string finalUrl;

        if (finalSameSite)
        {
            string keepMeSignedInParam = $"KeepMeSignedIn={finalKeepMeSignedIn}";
            string separator = referer.Contains('?') ? "&" : "?";
            finalUrl = $"{referer}{separator}{keepMeSignedInParam}&requestId={requestId}";

            if (finalIsMobileApp)
                finalUrl += "&isMobileApp=true";
        }
        else
        {
            // For external websites, add the user authentication data
            string separator = referer.Contains('?') ? "&" : "?";
            finalUrl = $"{referer}{separator}requestId={requestId}&keepMeSignedIn={finalKeepMeSignedIn}";

            if (finalIsMobileApp)
                finalUrl += "&isMobileApp=true";
        }

        return new RedirectResult(finalUrl);
    }

    private static string AddQueryString(string url, string queryString) =>
        $"{url}{(url.Contains('?') ? "&" : "?")}{queryString}";

    public Task<IActionResult> UpdateAuth(string referer, string? userInfo, bool isMobileApp = false)
    {
        if (!IsAllowedRedirect(referer))
            return Task.FromResult<IActionResult>(new BadRequestObjectResult("The requested return URL is not allowed."));

        // This legacy endpoint used to copy caller-controlled text directly into
        // an authentication cookie. It cannot be made safe without a trusted,
        // server-side exchange, so it is intentionally retired.
        return Task.FromResult<IActionResult>(new StatusCodeResult(Microsoft.AspNetCore.Http.StatusCodes.Status410Gone));
    }

    public async Task<IActionResult> Logout(string? referer, bool isMobileApp = false)
    {
        if (!IsAllowedRedirect(referer))
            return new BadRequestObjectResult("The requested return URL is not allowed.");

        await _request.HttpContext.SignOutAsync();

        string logoutUrl = !string.IsNullOrEmpty(referer) ? referer : "/";
        if (isMobileApp)
            logoutUrl = CloudLoginShared.AppendQueryParameter(logoutUrl, "isMobileApp", "true");

        return new RedirectResult(logoutUrl);
    }
}
