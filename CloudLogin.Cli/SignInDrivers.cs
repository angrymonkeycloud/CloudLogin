using System.Net;
using System.Text.Json;

namespace AngryMonkey.CloudLogin.Cli;

public sealed class CliSignInException(string message) : Exception(message);

internal static class CliJson
{
    /// <summary>The authority answers in PascalCase or camelCase depending on how its host formats JSON; both are read.</summary>
    public static System.Text.Json.JsonSerializerOptions ReadOptions { get; } = Create();

    private static System.Text.Json.JsonSerializerOptions Create()
    {
        System.Text.Json.JsonSerializerOptions options = CloudLoginSerialization.Options;
        options.PropertyNameCaseInsensitive = true;
        return options;
    }
}

/// <summary>Does what the person does in the system browser: starts at the login URL and ends at the application's callback URL.</summary>
public interface ISignInDriver
{
    string Name { get; }

    /// <returns>The callback URL the browser was handed back to the application with.</returns>
    Task<string> SignInAsync(BrowserSession browser, string loginUrl, CancellationToken cancellationToken = default);
}

/// <summary>The authority's own sign-in page, reached from the backend's native login URL.</summary>
public sealed record AuthorityPage(Uri Authority, string Reference)
{
    public string CompleteUrl => $"{Authority.GetLeftPart(UriPartial.Authority)}/CloudLogin/Login/Complete?referer={Uri.EscapeDataString(Reference)}";

    public string Url(string path) => $"{Authority.GetLeftPart(UriPartial.Authority)}/{path.TrimStart('/')}";

    public static async Task<AuthorityPage> OpenAsync(BrowserSession browser, string loginUrl, CancellationToken cancellationToken)
    {
        BrowserPage page = await browser.NavigateAsync(loginUrl, cancellationToken: cancellationToken);

        if (page.ReachedApplication)
            throw new CliSignInException($"The backend returned to the application before any sign-in ({SafeQuery(page)}).");

        if ((int)page.Status >= 500)
            throw new CliSignInException($"The sign-in page answered {(int)page.Status} at {page.Url.GetLeftPart(UriPartial.Path)}.");

        string? reference = page.Query("referer");

        if (string.IsNullOrWhiteSpace(reference))
            throw new CliSignInException($"The backend did not send the browser to a CloudLogin sign-in page (it ended at {page.Url.GetLeftPart(UriPartial.Path)}).");

        return new AuthorityPage(page.Url, reference);
    }

    /// <summary>What the sign-in page does for someone already signed in at the authority: asks it to finish, then follows its answer.</summary>
    public async Task<BrowserPage> CompleteAsync(BrowserSession browser, CancellationToken cancellationToken)
    {
        BrowserPage complete = await browser.NavigateAsync(CompleteUrl, cancellationToken: cancellationToken);

        if (complete.Status == HttpStatusCode.Unauthorized)
            throw new CliSignInException("The authority has no signed-in person in this browser.");

        if (complete.Status != HttpStatusCode.OK)
            throw new CliSignInException($"The authority did not complete the sign-in ({(int)complete.Status}): {Trim(complete.Body)}");

        string destination = complete.Body.TrimStart().StartsWith('"') ? JsonSerializer.Deserialize<string>(complete.Body) ?? string.Empty : complete.Body.Trim();

        if (!Uri.TryCreate(destination, UriKind.Absolute, out _))
            throw new CliSignInException("The authority answered without a destination to return to.");

        return await browser.NavigateAsync(destination, cancellationToken: cancellationToken);
    }

    internal static string SafeQuery(BrowserPage page) =>
        page.Query("error") is { } error ? $"error={error}" : page.Query("handoff") is not null ? "a handoff" : page.Url.GetLeftPart(UriPartial.Path);

    internal static string Trim(string text) => text.Length <= 200 ? text : text[..200] + "…";
}

/// <summary>Signs in through the authority's Test Mode, which a local or test deployment enables. No identity provider is involved.</summary>
public sealed class TestModeSignInDriver(string? testUser, bool createIfMissing) : ISignInDriver
{
    public string Name => "testmode";

    public async Task<string> SignInAsync(BrowserSession browser, string loginUrl, CancellationToken cancellationToken = default)
    {
        AuthorityPage authority = await AuthorityPage.OpenAsync(browser, loginUrl, cancellationToken);
        CloudUser user = await FindOrCreateUserAsync(browser, authority, cancellationToken);

        BrowserPage signedIn = await browser.NavigateAsync(
            authority.Url("CloudLogin/Login/TestSignIn"),
            HttpMethod.Post,
            new Dictionary<string, string> { ["userId"] = user.Id.ToString(), ["keepMeSignedIn"] = "false" },
            cancellationToken);

        if (signedIn.Status != HttpStatusCode.OK)
            throw new CliSignInException($"The authority refused the Test Mode sign-in for '{user.DisplayName}' ({(int)signedIn.Status}).");

        return (await authority.CompleteAsync(browser, cancellationToken)).Url.ToString();
    }

    private async Task<CloudUser> FindOrCreateUserAsync(BrowserSession browser, AuthorityPage authority, CancellationToken cancellationToken)
    {
        BrowserPage listed = await browser.NavigateAsync(authority.Url("CloudLogin/User/GetTestUsers"), cancellationToken: cancellationToken);

        if (listed.Status != HttpStatusCode.OK)
            throw new CliSignInException("This authority does not offer Test Mode. Use '--mode password' or '--mode paste' instead.");

        List<CloudUser> users = JsonSerializer.Deserialize<List<CloudUser>>(listed.Body, CliJson.ReadOptions) ?? [];
        CloudUser? match = string.IsNullOrWhiteSpace(testUser) ? users.FirstOrDefault() : users.FirstOrDefault(user => Matches(user, testUser));

        if (match is not null)
            return match;

        if (!createIfMissing)
            throw new CliSignInException(string.IsNullOrWhiteSpace(testUser)
                ? "The authority has no test users. Add '--create-test-user' to create one."
                : $"No test user is named '{testUser}'. Add '--create-test-user' to create it.");

        return await CreateAsync(browser, authority, string.IsNullOrWhiteSpace(testUser) ? "CloudLogin Cli" : testUser, cancellationToken);
    }

    private static async Task<CloudUser> CreateAsync(BrowserSession browser, AuthorityPage authority, string name, CancellationToken cancellationToken)
    {
        string[] parts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string first = parts[0];
        string last = parts.Length > 1 ? parts[1] : "Test";
        string email = $"cli-{Guid.NewGuid():N}@cloudlogin.test";

        BrowserPage created = await browser.NavigateAsync(
            authority.Url("CloudLogin/Login/PasswordRegistration"),
            HttpMethod.Post,
            new Dictionary<string, string> { ["input"] = email, ["inputFormat"] = "EmailAddress", ["password"] = string.Empty, ["firstName"] = first, ["lastName"] = last },
            cancellationToken);

        if (created.Status != HttpStatusCode.OK)
            throw new CliSignInException($"The authority did not create the test user '{name}' ({(int)created.Status}).");

        BrowserPage listed = await browser.NavigateAsync(authority.Url("CloudLogin/User/GetTestUsers"), cancellationToken: cancellationToken);
        List<CloudUser> users = listed.Status == HttpStatusCode.OK ? JsonSerializer.Deserialize<List<CloudUser>>(listed.Body, CliJson.ReadOptions) ?? [] : [];

        return users.FirstOrDefault(user => user.Inputs.Any(input => string.Equals(input.Input, email, StringComparison.OrdinalIgnoreCase)))
            ?? users.FirstOrDefault(user => Matches(user, name))
            ?? throw new CliSignInException($"The authority created the test user '{name}' but does not list it.");
    }

    internal static bool Matches(CloudUser user, string selector) =>
        string.Equals(user.DisplayName, selector, StringComparison.OrdinalIgnoreCase)
        || string.Equals($"{user.FirstName} {user.LastName}".Trim(), selector, StringComparison.OrdinalIgnoreCase)
        || string.Equals(user.PrimaryEmailAddress?.Input, selector, StringComparison.OrdinalIgnoreCase)
        || Guid.TryParse(selector, out Guid id) && user.Id == id;
}

/// <summary>Signs in with an email address and password at the authority's password provider.</summary>
public sealed class PasswordSignInDriver(string email, string password) : ISignInDriver
{
    public string Name => "password";

    public async Task<string> SignInAsync(BrowserSession browser, string loginUrl, CancellationToken cancellationToken = default)
    {
        AuthorityPage authority = await AuthorityPage.OpenAsync(browser, loginUrl, cancellationToken);

        BrowserPage signedIn = await browser.NavigateAsync(
            authority.Url("CloudLogin/Login/PasswordSignIn"),
            HttpMethod.Post,
            new Dictionary<string, string> { ["email"] = email, ["password"] = password, ["keepMeSignedIn"] = "false" },
            cancellationToken);

        if (signedIn.Status != HttpStatusCode.OK)
            throw new CliSignInException($"The authority refused the password sign-in ({(int)signedIn.Status}): {AuthorityPage.Trim(signedIn.Body)}");

        return (await authority.CompleteAsync(browser, cancellationToken)).Url.ToString();
    }
}

/// <summary>
/// For any deployment and any provider: the person signs in in their own browser, which then tries to open the application's address.
/// No application answers it on this computer, so the person copies that address from the browser and pastes it here.
/// </summary>
public sealed class PasteSignInDriver(TextWriter output, TextReader input, Action<string>? openBrowser) : ISignInDriver
{
    public string Name => "paste";

    public async Task<string> SignInAsync(BrowserSession browser, string loginUrl, CancellationToken cancellationToken = default)
    {
        await output.WriteLineAsync("Open this address in a browser and sign in:");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"  {loginUrl}");
        await output.WriteLineAsync();
        await output.WriteLineAsync("At the end the browser tries to open an address that starts with the application's scheme and cannot.");
        await output.WriteLineAsync("Copy that whole address from the address bar and paste it here:");
        await output.FlushAsync(cancellationToken);

        openBrowser?.Invoke(loginUrl);

        string? pasted = await input.ReadLineAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(pasted) ? throw new CliSignInException("No address was pasted.") : pasted.Trim();
    }
}
