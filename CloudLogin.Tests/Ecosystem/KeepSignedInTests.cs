using System.Net;

namespace AngryMonkey.CloudLogin.Tests.Ecosystem;

/// <summary>
/// "Keep me signed in" is ticked on the authority's page, and has to reach the application's own session: without it the
/// application's cookie ended with the browser, whatever the person chose.
/// </summary>
public class KeepSignedInTests
{
    private static async Task SignInAsync(TestBrowser browser, bool keep)
    {
        BrowserResponse signedIn = await browser.NavigateAsync(
            $"{AuthorityHost.Origin}/CloudLogin/Login/PasswordSignIn",
            HttpMethod.Post,
            new Dictionary<string, string>
            {
                ["email"] = AuthEcosystem.AuthorityEmail,
                ["password"] = AuthEcosystem.Password,
                ["keepMeSignedIn"] = keep.ToString()
            },
            initiatorHost: new Uri(AuthorityHost.Origin).Host);

        Assert.Equal(HttpStatusCode.OK, signedIn.Status);
    }

    private static DateTimeOffset? SessionCookieExpiry(TestBrowser browser, ConsumerHost app)
    {
        string name = Assert.Single(browser.Cookies, cookie => cookie.Host == new Uri(app.Origin).Host && !cookie.Name.Contains(".tx.", StringComparison.Ordinal)).Name;
        return browser.CookieExpiresOn(new Uri(app.Origin).Host, name);
    }

    [Fact]
    public async Task Choosing_to_stay_signed_in_gives_the_application_a_cookie_that_outlives_the_browser()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, app);
        await SignInAsync(browser, keep: true);
        await EcosystemFlow.CompleteAtAuthorityAsync(browser, reference);

        Assert.True((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
        DateTimeOffset? expires = SessionCookieExpiry(browser, app);
        Assert.NotNull(expires);
        Assert.InRange(expires.Value, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));
    }

    [Fact]
    public async Task Not_choosing_it_leaves_a_cookie_that_ends_with_the_browser()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, app);
        await SignInAsync(browser, keep: false);
        await EcosystemFlow.CompleteAtAuthorityAsync(browser, reference);

        Assert.True((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
        Assert.Null(SessionCookieExpiry(browser, app));
    }

    [Fact]
    public async Task The_choice_carries_to_the_next_application_reached_without_signing_in_again()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost first = await ecosystem.AddConsumerAsync("app-a");
        ConsumerHost second = await ecosystem.AddConsumerAsync("app-b");
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, first);
        await SignInAsync(browser, keep: true);
        await EcosystemFlow.CompleteAtAuthorityAsync(browser, reference);

        await EcosystemFlow.LoginAsync(browser, second, authorityAlreadySignedIn: true);

        Assert.True((await EcosystemFlow.MeAsync(browser, second)).Authenticated);
        Assert.NotNull(SessionCookieExpiry(browser, second));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Signing_in_with_a_provider_carries_the_choice_through_its_redirect(bool keep)
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, app);
        BrowserResponse landed = await browser.NavigateAsync(
            $"{AuthorityHost.Origin}/test/provider-returned?referer={Uri.EscapeDataString(reference)}&keepMeSignedIn={keep}",
            initiatorHost: new Uri(AuthorityHost.Origin).Host);

        Assert.True((await EcosystemFlow.MeAsync(browser, app)).Authenticated, $"{landed.Status} {landed.FinalUrl} {landed.Body}");
        Assert.Equal(keep, SessionCookieExpiry(browser, app) is not null);
    }

    [Fact]
    public async Task How_long_it_lasts_is_the_applications_to_set()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a", configureServer: server => server.KeepSignedInDuration = TimeSpan.FromDays(7));
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, app);
        await SignInAsync(browser, keep: true);
        await EcosystemFlow.CompleteAtAuthorityAsync(browser, reference);

        Assert.InRange(SessionCookieExpiry(browser, app)!.Value, DateTimeOffset.UtcNow.AddDays(6), DateTimeOffset.UtcNow.AddDays(8));
    }

    [Fact]
    public async Task Signing_out_still_ends_a_session_kept_past_the_browser()
    {
        await using AuthEcosystem ecosystem = await AuthEcosystem.CreateAsync();
        ConsumerHost app = await ecosystem.AddConsumerAsync("app-a");
        TestBrowser browser = ecosystem.NewBrowser();

        (_, string reference) = await EcosystemFlow.StartLoginAsync(browser, app);
        await SignInAsync(browser, keep: true);
        await EcosystemFlow.CompleteAtAuthorityAsync(browser, reference);
        Assert.True((await EcosystemFlow.MeAsync(browser, app)).Authenticated);

        await browser.NavigateAsync(EcosystemFlow.Url(app, "/auth/logout"), initiatorHost: new Uri(app.Origin).Host);

        Assert.False((await EcosystemFlow.MeAsync(browser, app)).Authenticated);
    }
}
