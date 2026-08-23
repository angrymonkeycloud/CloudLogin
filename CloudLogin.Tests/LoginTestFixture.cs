using AngryMonkey.Cloud;
using AngryMonkey.CloudLogin.Server;
using AngryMonkey.CloudLogin.Sever.Providers;
using AngryMonkey.CloudBlazor.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace AngryMonkey.CloudLogin.Tests;

internal sealed class LoginTestFixture
{
    public LoginTestFixture(
        bool testModeEnabled = false,
        IEnumerable<string>? allowedOrigins = null,
        IEnumerable<string>? allowedMobileSchemes = null,
        ICloudLoginEventPublisher? eventPublisher = null)
    {
        Configuration = new CloudLoginWebConfiguration
        {
            BaseAddress = "https://login.example:443",
            LoginDuration = TimeSpan.FromDays(14),
            WebConfig = static _ => { },
            AllowedRedirectOrigins = [.. allowedOrigins ?? []],
            AllowedMobileSchemes = [.. allowedMobileSchemes ?? []]
        };

        if (testModeEnabled)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TestMode:IsEnabled"] = "true",
                    ["TestMode:Label"] = "Test Mode"
                })
                .Build();

            Configuration.Providers.Add(new LoginTestProviders.TestModeConfiguration(
                configuration.GetSection("TestMode")));
        }

        HttpContext.Request.Scheme = "https";
        HttpContext.Request.Host = new HostString("login.example", 443);
        HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(Authentication)
            .BuildServiceProvider();

        Accessor.HttpContext = HttpContext;
        Server = new CloudLoginServer(
            new CloudGeographyClient(),
            Configuration,
            Accessor,
            cloudLoginStore: Store,
            eventPublisher: eventPublisher);
    }

    public CloudLoginWebConfiguration Configuration { get; }
    public InMemoryCloudLoginStore Store { get; } = new();
    public RecordingAuthenticationService Authentication { get; } = new();
    public DefaultHttpContext HttpContext { get; } = new();
    public HttpContextAccessor Accessor { get; } = new();
    public CloudLoginServer Server { get; }

    public void AuthenticateAs(CloudUser user)
    {
        ClaimsIdentity identity = new(
        [
            new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName ?? string.Empty),
            new Claim(ClaimTypes.UserData, System.Text.Json.JsonSerializer.Serialize(user, CloudLoginSerialization.Options))
        ], "UnitTest");

        HttpContext.User = new ClaimsPrincipal(identity);
        HttpContext.Request.Headers.Cookie = $"{Configuration.CookieName}=unit-test-cookie";
    }

    public async Task<CloudUser> AddPasswordUserAsync(
        string email = "person@example.com",
        string password = "Valid#123456",
        bool isTest = false)
    {
        CloudUser user = CreateUser(email, isTest);
        if (!isTest)
        {
            user.Inputs[0].Providers.Add(new CloudLoginProvider
            {
                Code = "Password",
                PasswordHash = await Server.HashPassword(password)
            });
        }

        Store.Users[user.ID] = user;
        return user;
    }

    public static CloudUser CreateUser(string email = "person@example.com", bool isTest = false) => new()
    {
        ID = Guid.NewGuid(),
        FirstName = "Test",
        LastName = "Person",
        DisplayName = "Test Person",
        IsTest = isTest,
        CreatedOn = DateTimeOffset.UtcNow.AddDays(-1),
        Inputs =
        [
            new CloudLoginInput
            {
                Input = email,
                Format = CloudLoginInputFormat.EmailAddress,
                IsPrimary = true
            }
        ]
    };
}

internal sealed class RecordingAuthenticationService : IAuthenticationService
{
    public ClaimsPrincipal? SignedInPrincipal { get; private set; }
    public AuthenticationProperties? SignedInProperties { get; private set; }
    public int SignInCount { get; private set; }
    public int SignOutCount { get; private set; }

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
        Task.FromResult(AuthenticateResult.NoResult());

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
        Task.CompletedTask;

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
        Task.CompletedTask;

    public Task SignInAsync(
        HttpContext context,
        string? scheme,
        ClaimsPrincipal principal,
        AuthenticationProperties? properties)
    {
        SignedInPrincipal = principal;
        SignedInProperties = properties;
        SignInCount++;
        context.User = principal;
        return Task.CompletedTask;
    }

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
    {
        SignOutCount++;
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCloudLoginStore : ICloudLoginStore
{
    public Dictionary<Guid, CloudUser> Users { get; } = [];
    public Dictionary<Guid, Guid> Requests { get; } = [];
    public int UpdateCount { get; private set; }
    public int CreateRequestCount { get; private set; }

    public Task<List<CloudUser>> GetUsers() => Task.FromResult(Users.Values.ToList());

    public Task<CloudUser?> GetUserById(Guid id) =>
        Task.FromResult(Users.GetValueOrDefault(id));

    public Task<List<CloudUser>> GetUsersByDisplayName(string displayName) =>
        Task.FromResult(Users.Values.Where(user =>
            string.Equals(user.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)).ToList());

    public async Task<CloudUser?> GetUserByDisplayName(string displayName) =>
        (await GetUsersByDisplayName(displayName)).FirstOrDefault();

    public Task<CloudUser?> GetUserByInput(string input) =>
        Task.FromResult(FindByInput(input));

    public Task<CloudUser?> GetUserByEmailAddress(string emailAddress) =>
        Task.FromResult(FindByInput(emailAddress, CloudLoginInputFormat.EmailAddress));

    public Task<CloudUser?> GetUserByPhoneNumber(string number) =>
        Task.FromResult(FindByInput(number, CloudLoginInputFormat.PhoneNumber));

    public Task<CloudUser?> GetUserByRequestId(Guid requestId)
    {
        if (!Requests.Remove(requestId, out Guid userId))
            return Task.FromResult<CloudUser?>(null);

        return GetUserById(userId);
    }

    public Task<AngryMonkey.CloudLogin.Server.CloudRequest> CreateRequest(Guid userId, Guid? requestId = null)
    {
        Guid id = requestId ?? Guid.NewGuid();
        Requests[id] = userId;
        CreateRequestCount++;

        AngryMonkey.CloudLogin.Server.CloudRequest request = new() { UserId = userId };
        request.SetId(id);
        return Task.FromResult(request);
    }

    public Task Update(CloudUser user)
    {
        Users[user.ID] = user;
        UpdateCount++;
        return Task.CompletedTask;
    }

    public Task Create(CloudUser user)
    {
        Users[user.ID] = user;
        return Task.CompletedTask;
    }

    public Task DeleteUser(Guid userId)
    {
        Users.Remove(userId);
        return Task.CompletedTask;
    }

    public Task AddInput(Guid userId, CloudLoginInput input)
    {
        Users[userId].Inputs.Add(input);
        return Task.CompletedTask;
    }

    public Task<int> GetUserCount() => Task.FromResult(Users.Count);

    private CloudUser? FindByInput(string input, CloudLoginInputFormat? format = null) =>
        Users.Values.FirstOrDefault(user => user.Inputs.Any(candidate =>
            (!format.HasValue || candidate.Format == format.Value) &&
            string.Equals(candidate.Input, input, StringComparison.OrdinalIgnoreCase)));
}
