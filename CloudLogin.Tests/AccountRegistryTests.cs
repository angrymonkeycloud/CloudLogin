using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Services;
using AngryMonkey.CloudLogin.Server;

namespace AngryMonkey.CloudLogin.Tests;

public class AccountRegistryTests
{
    [Fact]
    public async Task CreateAsync_workspace_registers_owner_membership()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry registry = new(store);
        Guid ownerId = Guid.NewGuid();

        CloudWorkspace workspace = await registry.CreateAsync("  Angry Monkey  ", ownerId);
        IReadOnlyList<CloudWorkspaceMember> members = await registry.GetMembersAsync(workspace.Id);

        CloudWorkspaceMember owner = Assert.Single(members);
        Assert.Equal("Angry Monkey", workspace.Name);
        Assert.True(owner.IsOwner);
        Assert.Equal(ownerId, owner.UserId);
        Assert.Equal(["Owner"], owner.Roles);
    }

    [Fact]
    public async Task AddMemberAsync_unknown_workspace_is_rejected()
    {
        WorkspaceRegistry registry = new(new InMemoryCloudLoginAccountStore());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => registry.AddMemberAsync(Guid.NewGuid(), Guid.NewGuid(), ["Developer"]));
    }

    [Fact]
    public async Task InviteAsync_creates_trimmed_expiring_invitation()
    {
        WorkspaceRegistry registry = new(new InMemoryCloudLoginAccountStore());
        Guid ownerId = Guid.NewGuid();
        CloudWorkspace workspace = await registry.CreateAsync("Cedar Labs", ownerId);
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(7);

        CloudWorkspaceInvitation invitation = await registry.InviteAsync(workspace.Id, "  developer@example.invalid  ", ownerId, expiry, ["Developer"]);

        Assert.Equal("developer@example.invalid", invitation.Recipient);
        Assert.Equal(ownerId, invitation.InvitedByUserId);
        Assert.Equal(["Developer"], invitation.Roles);
        Assert.Equal(expiry, invitation.ExpiresOn);
    }

    [Fact]
    public async Task InviteAsync_expired_invitation_is_rejected()
    {
        WorkspaceRegistry registry = new(new InMemoryCloudLoginAccountStore());
        Guid ownerId = Guid.NewGuid();
        CloudWorkspace workspace = await registry.CreateAsync("Cedar Labs", ownerId);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => registry.InviteAsync(workspace.Id, "developer@example.invalid", ownerId, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public async Task GetActiveAsync_excludes_expired_and_cancelled_subscriptions()
    {
        InMemoryCloudLoginAccountStore store = new();
        ICloudLoginSubscriptionRegistry registry = new SubscriptionRegistry(store);
        Guid userId = Guid.NewGuid();
        await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "active" });
        await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "expired", ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "cancelled", Status = CloudSubscriptionStatuses.Cancelled });

        IReadOnlyList<CloudSubscription> subscriptions = await registry.GetActiveAsync(userId);

        CloudSubscription subscription = Assert.Single(subscriptions);
        Assert.Equal("active", subscription.Reference);
    }

    [Fact]
    public async Task HasActiveAsync_application_metadata_remains_application_owned()
    {
        InMemoryCloudLoginAccountStore store = new();
        ICloudLoginSubscriptionRegistry registry = new SubscriptionRegistry(store);
        Guid userId = Guid.NewGuid();
        Dictionary<string, JsonElement> metadata = new()
        {
            ["credits"] = JsonSerializer.SerializeToElement(10_000),
            ["premiumModels"] = JsonSerializer.SerializeToElement(true)
        };
        await registry.SaveAsync(new() { UserId = userId, Application = "ai", Reference = "pro", Metadata = metadata });

        Assert.True(await registry.HasActiveAsync("AI", "PRO", userId));
        CloudSubscription saved = Assert.Single(await store.GetSubscriptionsAsync(userId, null));
        Assert.Equal(10_000, saved.Metadata["credits"].GetInt32());
        Assert.True(saved.Metadata["premiumModels"].GetBoolean());
    }

    [Fact]
    public async Task SaveAsync_without_account_owner_is_rejected()
    {
        ICloudLoginSubscriptionRegistry registry = new SubscriptionRegistry(new InMemoryCloudLoginAccountStore());
        CloudSubscription subscription = new() { Application = "studio", Reference = "pro" };

        await Assert.ThrowsAsync<ArgumentException>(() => registry.SaveAsync(subscription));
    }

    [Fact]
    public async Task Billing_profile_round_trips_provider_references()
    {
        InMemoryCloudLoginAccountStore store = new();
        Guid workspaceId = Guid.NewGuid();
        CloudBillingProfile profile = new()
        {
            WorkspaceId = workspaceId,
            ProviderCustomerReference = "cus_demo_123",
            PaymentMethods =
            [
                new("stripe", "pm_demo_visa", "Visa ending 4242", true),
                new("myfatoorah", "token_demo_mada", "Mada ending 0008")
            ]
        };

        await store.SaveBillingProfileAsync(profile);
        CloudBillingProfile? saved = await store.GetBillingProfileAsync(null, workspaceId);

        Assert.NotNull(saved);
        Assert.Equal("cus_demo_123", saved.ProviderCustomerReference);
        Assert.Equal(2, saved.PaymentMethods.Count);
        Assert.Single(saved.PaymentMethods, method => method.IsDefault);
    }

    [Fact]
    public async Task Registry_mutations_publish_versioned_identifier_events()
    {
        RecordingEventPublisher publisher = new();
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry workspaces = new(store, publisher);
        Guid ownerId = Guid.NewGuid();
        Guid memberId = Guid.NewGuid();

        CloudWorkspace workspace =
            await workspaces.CreateAsync("Acme", ownerId);
        await workspaces.AddMemberAsync(workspace.Id, memberId);
        await workspaces.InviteAsync(
            workspace.Id,
            "member@example.invalid",
            ownerId,
            DateTimeOffset.UtcNow.AddDays(1));
        workspace.Name = "Acme Updated";
        await workspaces.UpdateAsync(workspace, ownerId);

        SubscriptionRegistry subscriptions = new(store, publisher);
        Guid subscriptionId = Guid.NewGuid();
        await subscriptions.SaveAsync(new CloudSubscription
        {
            Id = subscriptionId,
            WorkspaceId = workspace.Id,
            Application = "portal",
            Reference = "pro"
        });
        await subscriptions.SaveAsync(new CloudSubscription
        {
            Id = subscriptionId,
            WorkspaceId = workspace.Id,
            Application = "portal",
            Reference = "pro",
            AutoRenew = true
        });
        await subscriptions.SaveAsync(new CloudSubscription
        {
            Id = subscriptionId,
            WorkspaceId = workspace.Id,
            Application = "portal",
            Reference = "pro",
            Status = CloudSubscriptionStatuses.Cancelled
        });

        Assert.Equal(
            [
                "Workspace.Created",
                "Workspace.MembershipUpdated",
                "Workspace.InvitationCreated",
                "Workspace.Updated",
                "Subscription.Created",
                "Subscription.Updated",
                "Subscription.Cancelled"
            ],
            publisher.Events.Select(item => item.EventType));
        Assert.All(publisher.Events, item =>
        {
            Assert.Equal(1, item.Version);
            Assert.False(string.IsNullOrWhiteSpace(item.EventId));
            Assert.True(item.Timestamp <= DateTimeOffset.UtcNow);
        });
        Assert.Equal(
            workspace.Id.ToString(),
            publisher.Events[0].EntityId);
        Assert.Equal(
            subscriptionId.ToString(),
            publisher.Events[^1].EntityId);
    }

    // ── Workspace allowances ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_applies_the_default_caps_when_the_host_configures_none()
    {
        WorkspaceRegistry registry = new(new InMemoryCloudLoginAccountStore());
        Guid ownerId = Guid.NewGuid();

        CloudWorkspaceQuota quota = await registry.GetQuotaAsync(ownerId);

        Assert.Equal(CloudWorkspaceLimits.DefaultMaxOwnedPerUser, quota.MaxOwned);
        Assert.Equal(CloudWorkspaceLimits.DefaultMaxPerUser, quota.MaxTotal);
        Assert.True(quota.CanCreate);
    }

    [Fact]
    public async Task CreateAsync_stops_at_the_configured_owned_cap()
    {
        WorkspaceRegistry registry = new(
            new InMemoryCloudLoginAccountStore(),
            configuration: Configured(maxOwned: 2));
        Guid ownerId = Guid.NewGuid();

        await registry.CreateAsync("First", ownerId);
        await registry.CreateAsync("Second", ownerId);

        CloudWorkspaceLimitReachedException exception = await Assert.ThrowsAsync<CloudWorkspaceLimitReachedException>(
            () => registry.CreateAsync("Third", ownerId));

        Assert.Equal(CloudWorkspaceLimitKinds.Owned, exception.Kind);
        Assert.Equal(2, exception.Limit);

        CloudWorkspaceQuota quota = await registry.GetQuotaAsync(ownerId);
        Assert.Equal(2, quota.Owned);
        Assert.False(quota.CanCreate);
    }

    [Fact]
    public async Task AddMemberAsync_stops_at_the_configured_membership_cap()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry registry = new(store, configuration: Configured(maxOwned: 5, maxTotal: 2));
        Guid joinerId = Guid.NewGuid();

        // Each workspace gets its own owner, so only the joiner's allowance is under test.
        CloudWorkspace first = await registry.CreateAsync("First", Guid.NewGuid());
        CloudWorkspace second = await registry.CreateAsync("Second", Guid.NewGuid());
        CloudWorkspace third = await registry.CreateAsync("Third", Guid.NewGuid());

        await registry.AddMemberAsync(first.Id, joinerId);
        await registry.AddMemberAsync(second.Id, joinerId);

        CloudWorkspaceLimitReachedException exception = await Assert.ThrowsAsync<CloudWorkspaceLimitReachedException>(
            () => registry.AddMemberAsync(third.Id, joinerId));

        Assert.Equal(CloudWorkspaceLimitKinds.Membership, exception.Kind);
        Assert.Equal(2, exception.Limit);

        // Updating an existing membership's roles isn't a new membership, so the cap doesn't apply.
        CloudWorkspaceMember updated = await registry.AddMemberAsync(first.Id, joinerId, ["Developer"]);
        Assert.Equal(["Developer"], updated.Roles);
    }

    [Fact]
    public async Task Owned_cap_never_exceeds_the_membership_cap()
    {
        WorkspaceRegistry registry = new(
            new InMemoryCloudLoginAccountStore(),
            configuration: Configured(maxOwned: 10, maxTotal: 3));

        CloudWorkspaceQuota quota = await registry.GetQuotaAsync(Guid.NewGuid());

        Assert.Equal(3, quota.MaxOwned);
        Assert.Equal(3, quota.MaxTotal);
    }

    [Fact]
    public async Task Unlimited_cap_never_refuses_a_create()
    {
        WorkspaceRegistry registry = new(
            new InMemoryCloudLoginAccountStore(),
            configuration: Configured(maxOwned: CloudWorkspaceLimits.Unlimited, maxTotal: CloudWorkspaceLimits.Unlimited));
        Guid ownerId = Guid.NewGuid();

        for (int index = 0; index < 12; index++)
            await registry.CreateAsync($"Workspace {index}", ownerId);

        CloudWorkspaceQuota quota = await registry.GetQuotaAsync(ownerId);

        Assert.True(quota.OwnedIsUnlimited);
        Assert.True(quota.CanCreate);
        Assert.Equal(12, quota.Owned);
    }

    [Fact]
    public async Task A_zero_cap_stops_creation_while_invitations_still_work()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry restricted = new(store, configuration: Configured(maxOwned: 0));
        WorkspaceRegistry host = new(store);
        Guid userId = Guid.NewGuid();

        await Assert.ThrowsAsync<CloudWorkspaceLimitReachedException>(() => restricted.CreateAsync("Mine", userId));

        CloudWorkspace provisioned = await host.CreateAsync("Provisioned", Guid.NewGuid());
        await restricted.AddMemberAsync(provisioned.Id, userId);

        CloudWorkspaceQuota quota = await restricted.GetQuotaAsync(userId);
        Assert.Equal(1, quota.Total);
        Assert.False(quota.CanCreate);
    }

    // ── Deletion policy ──────────────────────────────────────────────────────

    [Fact]
    public async Task Subscriptions_default_to_being_removable_only_once_they_stop_running()
    {
        InMemoryCloudLoginAccountStore store = new();
        ICloudLoginSubscriptionRegistry registry = new SubscriptionRegistry(store);
        Guid userId = Guid.NewGuid();

        CloudSubscription running = await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "running" });
        CloudSubscription expired = await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "expired", ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1) });

        Assert.Equal(CloudSubscriptionDeletionPolicies.WhenExpired, running.DeletionPolicy);
        await Assert.ThrowsAsync<CloudSubscriptionDeletionBlockedException>(() => registry.DeleteAsync(running.Id));

        await registry.DeleteAsync(expired.Id);
        Assert.Null(await store.GetSubscriptionAsync(expired.Id));
    }

    [Fact]
    public async Task Deletion_policies_allow_and_forbid_removal_regardless_of_expiry()
    {
        InMemoryCloudLoginAccountStore store = new();
        ICloudLoginSubscriptionRegistry registry = new SubscriptionRegistry(store);
        Guid userId = Guid.NewGuid();

        CloudSubscription always = await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "always", DeletionPolicy = CloudSubscriptionDeletionPolicies.Always });
        CloudSubscription never = await registry.SaveAsync(new()
        {
            UserId = userId,
            Application = "studio",
            Reference = "never",
            Status = CloudSubscriptionStatuses.Expired,
            ExpiresOn = DateTimeOffset.UtcNow.AddYears(-1),
            DeletionPolicy = CloudSubscriptionDeletionPolicies.Never
        });

        await registry.DeleteAsync(always.Id);
        Assert.Null(await store.GetSubscriptionAsync(always.Id));

        await Assert.ThrowsAsync<CloudSubscriptionDeletionBlockedException>(() => registry.DeleteAsync(never.Id));
    }

    [Fact]
    public async Task Cancelled_subscriptions_are_removable_under_the_default_policy()
    {
        InMemoryCloudLoginAccountStore store = new();
        ICloudLoginSubscriptionRegistry registry = new SubscriptionRegistry(store);

        CloudSubscription cancelled = await registry.SaveAsync(new()
        {
            UserId = Guid.NewGuid(),
            Application = "studio",
            Reference = "cancelled",
            Status = CloudSubscriptionStatuses.Cancelled,
            ExpiresOn = DateTimeOffset.UtcNow.AddYears(1)
        });

        await registry.DeleteAsync(cancelled.Id);
        Assert.Null(await store.GetSubscriptionAsync(cancelled.Id));
    }

    // ── Workspace deletion ────────────────────────────────────────────────

    [Fact]
    public async Task An_workspace_with_a_running_subscription_cannot_be_deleted()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry workspaces = new(store);
        ICloudLoginSubscriptionRegistry subscriptions = new SubscriptionRegistry(store);
        Guid ownerId = Guid.NewGuid();

        CloudWorkspace workspace = await workspaces.CreateAsync("Cedar Labs", ownerId);
        await subscriptions.SaveAsync(new() { WorkspaceId = workspace.Id, Application = "portal", Reference = "team", ExpiresOn = DateTimeOffset.UtcNow.AddDays(30) });

        CloudWorkspaceDeletionReport report = await workspaces.GetDeletionReportAsync(workspace.Id, ownerId);

        Assert.False(report.CanDelete);
        Assert.Equal(CloudWorkspaceDeletionBlockers.ActiveSubscriptions, report.Blockers);
        Assert.Equal(1, report.ActiveSubscriptionCount);
        Assert.NotEmpty(report.Reasons);

        await Assert.ThrowsAsync<CloudWorkspaceDeletionBlockedException>(() => workspaces.DeleteAsync(workspace.Id, ownerId));
        Assert.NotNull(await store.GetWorkspaceAsync(workspace.Id));
    }

    [Fact]
    public async Task A_protected_subscription_blocks_deletion_even_after_it_expires()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry workspaces = new(store);
        ICloudLoginSubscriptionRegistry subscriptions = new SubscriptionRegistry(store);
        Guid ownerId = Guid.NewGuid();

        CloudWorkspace workspace = await workspaces.CreateAsync("Cedar Labs", ownerId);
        await subscriptions.SaveAsync(new()
        {
            WorkspaceId = workspace.Id,
            Application = "ledger",
            Reference = "archive",
            Status = CloudSubscriptionStatuses.Expired,
            ExpiresOn = DateTimeOffset.UtcNow.AddYears(-1),
            DeletionPolicy = CloudSubscriptionDeletionPolicies.Never
        });

        CloudWorkspaceDeletionReport report = await workspaces.GetDeletionReportAsync(workspace.Id, ownerId);

        Assert.False(report.CanDelete);
        Assert.Equal(CloudWorkspaceDeletionBlockers.ProtectedSubscriptions, report.Blockers);
        Assert.Equal(0, report.ActiveSubscriptionCount);
        Assert.Equal(1, report.ProtectedSubscriptionCount);
    }

    [Fact]
    public async Task Deleting_an_workspace_clears_its_members_invitations_billing_and_subscriptions()
    {
        RecordingEventPublisher publisher = new();
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry workspaces = new(store, publisher);
        ICloudLoginSubscriptionRegistry subscriptions = new SubscriptionRegistry(store);
        Guid ownerId = Guid.NewGuid();
        Guid memberId = Guid.NewGuid();

        CloudWorkspace workspace = await workspaces.CreateAsync("Cedar Labs", ownerId);
        await workspaces.AddMemberAsync(workspace.Id, memberId, ["Developer"]);
        await workspaces.InviteAsync(workspace.Id, "partner@example.invalid", ownerId, DateTimeOffset.UtcNow.AddDays(7));
        CloudSubscription expired = await subscriptions.SaveAsync(new()
        {
            WorkspaceId = workspace.Id,
            Application = "portal",
            Reference = "team",
            Status = CloudSubscriptionStatuses.Expired,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await store.SaveBillingProfileAsync(new() { WorkspaceId = workspace.Id, PaymentMethods = [new("stripe", "pm_demo", "Visa ending 4242", true)] });

        CloudWorkspaceDeletionReport report = await workspaces.GetDeletionReportAsync(workspace.Id, ownerId);
        Assert.True(report.CanDelete);
        Assert.Equal(1, report.OtherMemberCount);
        Assert.Equal(1, report.PaymentMethodCount);
        Assert.Equal(1, report.RemovableSubscriptionCount);

        await workspaces.DeleteAsync(workspace.Id, ownerId);

        Assert.Null(await store.GetWorkspaceAsync(workspace.Id));
        Assert.Empty(await store.GetMembersAsync(workspace.Id));
        Assert.Empty(await store.GetInvitationsAsync(workspace.Id));
        Assert.Null(await store.GetSubscriptionAsync(expired.Id));
        Assert.Null(await store.GetBillingProfileAsync(null, workspace.Id));
        Assert.Empty(await store.GetWorkspacesForUserAsync(ownerId));
        Assert.Contains("Workspace.Deleted", publisher.Events.Select(item => item.EventType));
    }

    [Fact]
    public async Task Only_an_owner_may_delete_an_workspace()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry workspaces = new(store);
        Guid ownerId = Guid.NewGuid();
        Guid memberId = Guid.NewGuid();

        CloudWorkspace workspace = await workspaces.CreateAsync("Cedar Labs", ownerId);
        await workspaces.AddMemberAsync(workspace.Id, memberId, ["Admin"]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspaces.DeleteAsync(workspace.Id, memberId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspaces.GetDeletionReportAsync(workspace.Id, memberId));

        await workspaces.DeleteAsync(workspace.Id, ownerId);
        Assert.Null(await store.GetWorkspaceAsync(workspace.Id));
    }

    [Fact]
    public async Task Deleting_an_workspace_frees_the_owner_allowance()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry workspaces = new(store, configuration: Configured(maxOwned: 1));
        Guid ownerId = Guid.NewGuid();

        CloudWorkspace first = await workspaces.CreateAsync("First", ownerId);
        await Assert.ThrowsAsync<CloudWorkspaceLimitReachedException>(() => workspaces.CreateAsync("Second", ownerId));

        await workspaces.DeleteAsync(first.Id, ownerId);

        CloudWorkspace second = await workspaces.CreateAsync("Second", ownerId);
        Assert.Equal("Second", second.Name);
    }

    [Fact]
    public async Task UpdateAsync_saves_the_full_billing_profile_and_trims_blanks()
    {
        InMemoryCloudLoginAccountStore store = new();
        WorkspaceRegistry workspaces = new(store);
        Guid ownerId = Guid.NewGuid();

        CloudWorkspace workspace = await workspaces.CreateAsync("Cedar Labs", ownerId);
        workspace.LegalName = "  Cedar Labs SARL  ";
        workspace.TaxId = "   ";
        workspace.Website = " https://cedar.example ";
        workspace.BillingAddress = new CloudWorkspaceAddress { Line1 = "  1 Cedar Street ", City = " Beirut ", Country = "  " };

        CloudWorkspace saved = await workspaces.UpdateAsync(workspace, ownerId);

        Assert.Equal("Cedar Labs SARL", saved.LegalName);
        Assert.Null(saved.TaxId);
        Assert.Equal("https://cedar.example", saved.Website);
        Assert.Equal("1 Cedar Street", saved.BillingAddress.Line1);
        Assert.Equal("Beirut", saved.BillingAddress.City);
        Assert.Null(saved.BillingAddress.Country);
        Assert.Equal("1 Cedar Street, Beirut", saved.BillingAddress.ToString());
        Assert.NotNull(saved.UpdatedOn);
    }

    private static CloudLoginWebConfiguration Configured(int? maxOwned = null, int? maxTotal = null)
        => new() { Workspace = new WorkspaceConfiguration { MaxOwnedPerUser = maxOwned, MaxPerUser = maxTotal } };

    private sealed class RecordingEventPublisher : ICloudLoginEventPublisher
    {
        public List<CloudLoginEvent> Events { get; } = [];

        public Task PublishAsync(
            CloudLoginEvent cloudEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(cloudEvent);
            return Task.CompletedTask;
        }
    }
}
