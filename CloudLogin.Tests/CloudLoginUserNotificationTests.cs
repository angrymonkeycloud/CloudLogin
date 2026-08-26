using AngryMonkey.CloudLogin;
using Xunit;

namespace AngryMonkey.CloudLogin.Tests;

/// <summary>
/// Delivery of the signed-in user notification.
/// <para>
/// The signed-in user is app-wide state (static backing fields on
/// <see cref="CloudLoginBaseService"/>), but the notification about it used to be a plain
/// instance event. The instance that establishes a session is routinely not the one
/// components hold - the WebAssembly bootstrappers resolved the service from a throwaway
/// child scope, and MAUI resolves it from the root provider while components get the
/// BlazorWebView's scope - so the user was signed in and nobody was told. The UI kept
/// rendering its signed-out state until a second click or a page reload forced it to
/// re-read the (already populated) static user, and the role loader never ran, which showed
/// as "no access" on the portal.
/// </para>
/// </summary>
public class CloudLoginUserNotificationTests
{
    /// <summary>Minimal concrete service; the notification plumbing lives in the base class.</summary>
    private sealed class TestLoginService : CloudLoginBaseService
    {
        public override Task Login() => Task.CompletedTask;
        public override Task BeginLoginAsync(string? returnUrl) => Task.CompletedTask;
        public override Task<string> ProfileUrl() => Task.FromResult("/");

        public void Publish(CloudUser? user) => RaiseUserChanged(user);
    }

    [Fact]
    public void UserChanged_ReachesSubscribersOfOtherInstances()
    {
        TestLoginService componentsInstance = new();
        TestLoginService bootstrapperInstance = new();

        CloudUser? received = null;
        int calls = 0;

        void Handler(CloudUser? user)
        {
            received = user;
            calls++;
        }

        componentsInstance.UserChanged += Handler;

        try
        {
            CloudUser signedIn = new() { ID = Guid.NewGuid(), DisplayName = "Test User" };

            // The bootstrapper's instance confirms the session; the subscriber that renders
            // the account UI is attached to a different instance entirely.
            bootstrapperInstance.Publish(signedIn);

            Assert.Equal(1, calls);
            Assert.Same(signedIn, received);
        }
        finally
        {
            componentsInstance.UserChanged -= Handler;
        }
    }

    [Fact]
    public void UserChanged_StopsAfterUnsubscribing()
    {
        TestLoginService instance = new();
        int calls = 0;

        void Handler(CloudUser? user) => calls++;

        instance.UserChanged += Handler;
        instance.UserChanged -= Handler;

        instance.Publish(new CloudUser { ID = Guid.NewGuid() });

        // App-wide delivery must not mean an undetachable subscription: a disposed
        // component would otherwise keep being called back forever.
        Assert.Equal(0, calls);
    }
}
