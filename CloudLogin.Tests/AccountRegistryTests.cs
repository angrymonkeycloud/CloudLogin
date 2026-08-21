using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Services;
using AngryMonkey.CloudLogin.Server;

namespace AngryMonkey.CloudLogin.Tests;

public class AccountRegistryTests
{
    [Fact]
    public async Task CreateAsync_organization_registers_owner_membership()
    {
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry registry = new(store);
        Guid ownerId = Guid.NewGuid();

        CloudLoginOrganization organization = await registry.CreateAsync("  Angry Monkey  ", ownerId);
        IReadOnlyList<CloudLoginOrganizationMember> members = await registry.GetMembersAsync(organization.Id);

        CloudLoginOrganizationMember owner = Assert.Single(members);
        Assert.Equal("Angry Monkey", organization.Name);
        Assert.True(owner.IsOwner);
        Assert.Equal(ownerId, owner.UserId);
        Assert.Equal(["Owner"], owner.Roles);
    }

    [Fact]
    public async Task AddMemberAsync_unknown_organization_is_rejected()
    {
        OrganizationRegistry registry = new(new InMemoryCloudLoginAccountStore());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => registry.AddMemberAsync(Guid.NewGuid(), Guid.NewGuid(), ["Developer"]));
    }

    [Fact]
    public async Task InviteAsync_creates_trimmed_expiring_invitation()
    {
        OrganizationRegistry registry = new(new InMemoryCloudLoginAccountStore());
        Guid ownerId = Guid.NewGuid();
        CloudLoginOrganization organization = await registry.CreateAsync("Cedar Labs", ownerId);
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(7);

        CloudLoginOrganizationInvitation invitation = await registry.InviteAsync(organization.Id, "  developer@example.invalid  ", ownerId, expiry, ["Developer"]);

        Assert.Equal("developer@example.invalid", invitation.Recipient);
        Assert.Equal(ownerId, invitation.InvitedByUserId);
        Assert.Equal(["Developer"], invitation.Roles);
        Assert.Equal(expiry, invitation.ExpiresOn);
    }

    [Fact]
    public async Task InviteAsync_expired_invitation_is_rejected()
    {
        OrganizationRegistry registry = new(new InMemoryCloudLoginAccountStore());
        Guid ownerId = Guid.NewGuid();
        CloudLoginOrganization organization = await registry.CreateAsync("Cedar Labs", ownerId);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => registry.InviteAsync(organization.Id, "developer@example.invalid", ownerId, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public async Task GetActiveAsync_excludes_expired_and_cancelled_subscriptions()
    {
        InMemoryCloudLoginAccountStore store = new();
        ISubscriptionRegistry registry = new SubscriptionRegistry(store);
        Guid userId = Guid.NewGuid();
        await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "active" });
        await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "expired", ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "cancelled", Status = AccountSubscriptionStatuses.Cancelled });

        IReadOnlyList<AccountSubscription> subscriptions = await registry.GetActiveAsync(userId);

        AccountSubscription subscription = Assert.Single(subscriptions);
        Assert.Equal("active", subscription.Reference);
    }

    [Fact]
    public async Task HasActiveAsync_application_metadata_remains_application_owned()
    {
        InMemoryCloudLoginAccountStore store = new();
        ISubscriptionRegistry registry = new SubscriptionRegistry(store);
        Guid userId = Guid.NewGuid();
        Dictionary<string, JsonElement> metadata = new()
        {
            ["credits"] = JsonSerializer.SerializeToElement(10_000),
            ["premiumModels"] = JsonSerializer.SerializeToElement(true)
        };
        await registry.SaveAsync(new() { UserId = userId, Application = "ai", Reference = "pro", Metadata = metadata });

        Assert.True(await registry.HasActiveAsync("AI", "PRO", userId));
        AccountSubscription saved = Assert.Single(await store.GetSubscriptionsAsync(userId, null));
        Assert.Equal(10_000, saved.Metadata["credits"].GetInt32());
        Assert.True(saved.Metadata["premiumModels"].GetBoolean());
    }

    [Fact]
    public async Task SaveAsync_without_account_owner_is_rejected()
    {
        ISubscriptionRegistry registry = new SubscriptionRegistry(new InMemoryCloudLoginAccountStore());
        AccountSubscription subscription = new() { Application = "studio", Reference = "pro" };

        await Assert.ThrowsAsync<ArgumentException>(() => registry.SaveAsync(subscription));
    }

    [Fact]
    public async Task Billing_profile_round_trips_provider_references()
    {
        InMemoryCloudLoginAccountStore store = new();
        Guid organizationId = Guid.NewGuid();
        AccountBillingProfile profile = new()
        {
            OrganizationId = organizationId,
            ProviderCustomerReference = "cus_demo_123",
            PaymentMethods =
            [
                new("stripe", "pm_demo_visa", "Visa ending 4242", true),
                new("myfatoorah", "token_demo_mada", "Mada ending 0008")
            ]
        };

        await store.SaveBillingProfileAsync(profile);
        AccountBillingProfile? saved = await store.GetBillingProfileAsync(null, organizationId);

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
        OrganizationRegistry organizations = new(store, publisher);
        Guid ownerId = Guid.NewGuid();
        Guid memberId = Guid.NewGuid();

        CloudLoginOrganization organization =
            await organizations.CreateAsync("Acme", ownerId);
        await organizations.AddMemberAsync(organization.Id, memberId);
        await organizations.InviteAsync(
            organization.Id,
            "member@example.invalid",
            ownerId,
            DateTimeOffset.UtcNow.AddDays(1));
        organization.Name = "Acme Updated";
        await organizations.UpdateAsync(organization, ownerId);

        SubscriptionRegistry subscriptions = new(store, publisher);
        Guid subscriptionId = Guid.NewGuid();
        await subscriptions.SaveAsync(new AccountSubscription
        {
            Id = subscriptionId,
            OrganizationId = organization.Id,
            Application = "portal",
            Reference = "pro"
        });
        await subscriptions.SaveAsync(new AccountSubscription
        {
            Id = subscriptionId,
            OrganizationId = organization.Id,
            Application = "portal",
            Reference = "pro",
            AutoRenew = true
        });
        await subscriptions.SaveAsync(new AccountSubscription
        {
            Id = subscriptionId,
            OrganizationId = organization.Id,
            Application = "portal",
            Reference = "pro",
            Status = AccountSubscriptionStatuses.Cancelled
        });

        Assert.Equal(
            [
                "Organization.Created",
                "Organization.MembershipUpdated",
                "Organization.InvitationCreated",
                "Organization.Updated",
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
            organization.Id.ToString(),
            publisher.Events[0].EntityId);
        Assert.Equal(
            subscriptionId.ToString(),
            publisher.Events[^1].EntityId);
    }

    // ── Organization allowances ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_applies_the_default_caps_when_the_host_configures_none()
    {
        OrganizationRegistry registry = new(new InMemoryCloudLoginAccountStore());
        Guid ownerId = Guid.NewGuid();

        OrganizationQuota quota = await registry.GetQuotaAsync(ownerId);

        Assert.Equal(OrganizationLimits.DefaultMaxOwnedPerUser, quota.MaxOwned);
        Assert.Equal(OrganizationLimits.DefaultMaxPerUser, quota.MaxTotal);
        Assert.True(quota.CanCreate);
    }

    [Fact]
    public async Task CreateAsync_stops_at_the_configured_owned_cap()
    {
        OrganizationRegistry registry = new(
            new InMemoryCloudLoginAccountStore(),
            configuration: Configured(maxOwned: 2));
        Guid ownerId = Guid.NewGuid();

        await registry.CreateAsync("First", ownerId);
        await registry.CreateAsync("Second", ownerId);

        OrganizationLimitReachedException exception = await Assert.ThrowsAsync<OrganizationLimitReachedException>(
            () => registry.CreateAsync("Third", ownerId));

        Assert.Equal(OrganizationLimitKinds.Owned, exception.Kind);
        Assert.Equal(2, exception.Limit);

        OrganizationQuota quota = await registry.GetQuotaAsync(ownerId);
        Assert.Equal(2, quota.Owned);
        Assert.False(quota.CanCreate);
    }

    [Fact]
    public async Task AddMemberAsync_stops_at_the_configured_membership_cap()
    {
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry registry = new(store, configuration: Configured(maxOwned: 5, maxTotal: 2));
        Guid joinerId = Guid.NewGuid();

        // Each organization gets its own owner, so only the joiner's allowance is under test.
        CloudLoginOrganization first = await registry.CreateAsync("First", Guid.NewGuid());
        CloudLoginOrganization second = await registry.CreateAsync("Second", Guid.NewGuid());
        CloudLoginOrganization third = await registry.CreateAsync("Third", Guid.NewGuid());

        await registry.AddMemberAsync(first.Id, joinerId);
        await registry.AddMemberAsync(second.Id, joinerId);

        OrganizationLimitReachedException exception = await Assert.ThrowsAsync<OrganizationLimitReachedException>(
            () => registry.AddMemberAsync(third.Id, joinerId));

        Assert.Equal(OrganizationLimitKinds.Membership, exception.Kind);
        Assert.Equal(2, exception.Limit);

        // Updating an existing membership's roles isn't a new membership, so the cap doesn't apply.
        CloudLoginOrganizationMember updated = await registry.AddMemberAsync(first.Id, joinerId, ["Developer"]);
        Assert.Equal(["Developer"], updated.Roles);
    }

    [Fact]
    public async Task Owned_cap_never_exceeds_the_membership_cap()
    {
        OrganizationRegistry registry = new(
            new InMemoryCloudLoginAccountStore(),
            configuration: Configured(maxOwned: 10, maxTotal: 3));

        OrganizationQuota quota = await registry.GetQuotaAsync(Guid.NewGuid());

        Assert.Equal(3, quota.MaxOwned);
        Assert.Equal(3, quota.MaxTotal);
    }

    [Fact]
    public async Task Unlimited_cap_never_refuses_a_create()
    {
        OrganizationRegistry registry = new(
            new InMemoryCloudLoginAccountStore(),
            configuration: Configured(maxOwned: OrganizationLimits.Unlimited, maxTotal: OrganizationLimits.Unlimited));
        Guid ownerId = Guid.NewGuid();

        for (int index = 0; index < 12; index++)
            await registry.CreateAsync($"Organization {index}", ownerId);

        OrganizationQuota quota = await registry.GetQuotaAsync(ownerId);

        Assert.True(quota.OwnedIsUnlimited);
        Assert.True(quota.CanCreate);
        Assert.Equal(12, quota.Owned);
    }

    [Fact]
    public async Task A_zero_cap_stops_creation_while_invitations_still_work()
    {
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry restricted = new(store, configuration: Configured(maxOwned: 0));
        OrganizationRegistry host = new(store);
        Guid userId = Guid.NewGuid();

        await Assert.ThrowsAsync<OrganizationLimitReachedException>(() => restricted.CreateAsync("Mine", userId));

        CloudLoginOrganization provisioned = await host.CreateAsync("Provisioned", Guid.NewGuid());
        await restricted.AddMemberAsync(provisioned.Id, userId);

        OrganizationQuota quota = await restricted.GetQuotaAsync(userId);
        Assert.Equal(1, quota.Total);
        Assert.False(quota.CanCreate);
    }

    // ── Deletion policy ──────────────────────────────────────────────────────

    [Fact]
    public async Task Subscriptions_default_to_being_removable_only_once_they_stop_running()
    {
        InMemoryCloudLoginAccountStore store = new();
        ISubscriptionRegistry registry = new SubscriptionRegistry(store);
        Guid userId = Guid.NewGuid();

        AccountSubscription running = await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "running" });
        AccountSubscription expired = await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "expired", ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-1) });

        Assert.Equal(SubscriptionDeletionPolicies.WhenExpired, running.DeletionPolicy);
        await Assert.ThrowsAsync<SubscriptionDeletionBlockedException>(() => registry.DeleteAsync(running.Id));

        await registry.DeleteAsync(expired.Id);
        Assert.Null(await store.GetSubscriptionAsync(expired.Id));
    }

    [Fact]
    public async Task Deletion_policies_allow_and_forbid_removal_regardless_of_expiry()
    {
        InMemoryCloudLoginAccountStore store = new();
        ISubscriptionRegistry registry = new SubscriptionRegistry(store);
        Guid userId = Guid.NewGuid();

        AccountSubscription always = await registry.SaveAsync(new() { UserId = userId, Application = "studio", Reference = "always", DeletionPolicy = SubscriptionDeletionPolicies.Always });
        AccountSubscription never = await registry.SaveAsync(new()
        {
            UserId = userId,
            Application = "studio",
            Reference = "never",
            Status = AccountSubscriptionStatuses.Expired,
            ExpiresOn = DateTimeOffset.UtcNow.AddYears(-1),
            DeletionPolicy = SubscriptionDeletionPolicies.Never
        });

        await registry.DeleteAsync(always.Id);
        Assert.Null(await store.GetSubscriptionAsync(always.Id));

        await Assert.ThrowsAsync<SubscriptionDeletionBlockedException>(() => registry.DeleteAsync(never.Id));
    }

    [Fact]
    public async Task Cancelled_subscriptions_are_removable_under_the_default_policy()
    {
        InMemoryCloudLoginAccountStore store = new();
        ISubscriptionRegistry registry = new SubscriptionRegistry(store);

        AccountSubscription cancelled = await registry.SaveAsync(new()
        {
            UserId = Guid.NewGuid(),
            Application = "studio",
            Reference = "cancelled",
            Status = AccountSubscriptionStatuses.Cancelled,
            ExpiresOn = DateTimeOffset.UtcNow.AddYears(1)
        });

        await registry.DeleteAsync(cancelled.Id);
        Assert.Null(await store.GetSubscriptionAsync(cancelled.Id));
    }

    // ── Organization deletion ────────────────────────────────────────────────

    [Fact]
    public async Task An_organization_with_a_running_subscription_cannot_be_deleted()
    {
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry organizations = new(store);
        ISubscriptionRegistry subscriptions = new SubscriptionRegistry(store);
        Guid ownerId = Guid.NewGuid();

        CloudLoginOrganization organization = await organizations.CreateAsync("Cedar Labs", ownerId);
        await subscriptions.SaveAsync(new() { OrganizationId = organization.Id, Application = "portal", Reference = "team", ExpiresOn = DateTimeOffset.UtcNow.AddDays(30) });

        OrganizationDeletionReport report = await organizations.GetDeletionReportAsync(organization.Id, ownerId);

        Assert.False(report.CanDelete);
        Assert.Equal(OrganizationDeletionBlockers.ActiveSubscriptions, report.Blockers);
        Assert.Equal(1, report.ActiveSubscriptionCount);
        Assert.NotEmpty(report.Reasons);

        await Assert.ThrowsAsync<OrganizationDeletionBlockedException>(() => organizations.DeleteAsync(organization.Id, ownerId));
        Assert.NotNull(await store.GetOrganizationAsync(organization.Id));
    }

    [Fact]
    public async Task A_protected_subscription_blocks_deletion_even_after_it_expires()
    {
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry organizations = new(store);
        ISubscriptionRegistry subscriptions = new SubscriptionRegistry(store);
        Guid ownerId = Guid.NewGuid();

        CloudLoginOrganization organization = await organizations.CreateAsync("Cedar Labs", ownerId);
        await subscriptions.SaveAsync(new()
        {
            OrganizationId = organization.Id,
            Application = "ledger",
            Reference = "archive",
            Status = AccountSubscriptionStatuses.Expired,
            ExpiresOn = DateTimeOffset.UtcNow.AddYears(-1),
            DeletionPolicy = SubscriptionDeletionPolicies.Never
        });

        OrganizationDeletionReport report = await organizations.GetDeletionReportAsync(organization.Id, ownerId);

        Assert.False(report.CanDelete);
        Assert.Equal(OrganizationDeletionBlockers.ProtectedSubscriptions, report.Blockers);
        Assert.Equal(0, report.ActiveSubscriptionCount);
        Assert.Equal(1, report.ProtectedSubscriptionCount);
    }

    [Fact]
    public async Task Deleting_an_organization_clears_its_members_invitations_billing_and_subscriptions()
    {
        RecordingEventPublisher publisher = new();
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry organizations = new(store, publisher);
        ISubscriptionRegistry subscriptions = new SubscriptionRegistry(store);
        Guid ownerId = Guid.NewGuid();
        Guid memberId = Guid.NewGuid();

        CloudLoginOrganization organization = await organizations.CreateAsync("Cedar Labs", ownerId);
        await organizations.AddMemberAsync(organization.Id, memberId, ["Developer"]);
        await organizations.InviteAsync(organization.Id, "partner@example.invalid", ownerId, DateTimeOffset.UtcNow.AddDays(7));
        AccountSubscription expired = await subscriptions.SaveAsync(new()
        {
            OrganizationId = organization.Id,
            Application = "portal",
            Reference = "team",
            Status = AccountSubscriptionStatuses.Expired,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await store.SaveBillingProfileAsync(new() { OrganizationId = organization.Id, PaymentMethods = [new("stripe", "pm_demo", "Visa ending 4242", true)] });

        OrganizationDeletionReport report = await organizations.GetDeletionReportAsync(organization.Id, ownerId);
        Assert.True(report.CanDelete);
        Assert.Equal(1, report.OtherMemberCount);
        Assert.Equal(1, report.PaymentMethodCount);
        Assert.Equal(1, report.RemovableSubscriptionCount);

        await organizations.DeleteAsync(organization.Id, ownerId);

        Assert.Null(await store.GetOrganizationAsync(organization.Id));
        Assert.Empty(await store.GetMembersAsync(organization.Id));
        Assert.Empty(await store.GetInvitationsAsync(organization.Id));
        Assert.Null(await store.GetSubscriptionAsync(expired.Id));
        Assert.Null(await store.GetBillingProfileAsync(null, organization.Id));
        Assert.Empty(await store.GetOrganizationsForUserAsync(ownerId));
        Assert.Contains("Organization.Deleted", publisher.Events.Select(item => item.EventType));
    }

    [Fact]
    public async Task Only_an_owner_may_delete_an_organization()
    {
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry organizations = new(store);
        Guid ownerId = Guid.NewGuid();
        Guid memberId = Guid.NewGuid();

        CloudLoginOrganization organization = await organizations.CreateAsync("Cedar Labs", ownerId);
        await organizations.AddMemberAsync(organization.Id, memberId, ["Admin"]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => organizations.DeleteAsync(organization.Id, memberId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => organizations.GetDeletionReportAsync(organization.Id, memberId));

        await organizations.DeleteAsync(organization.Id, ownerId);
        Assert.Null(await store.GetOrganizationAsync(organization.Id));
    }

    [Fact]
    public async Task Deleting_an_organization_frees_the_owner_allowance()
    {
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry organizations = new(store, configuration: Configured(maxOwned: 1));
        Guid ownerId = Guid.NewGuid();

        CloudLoginOrganization first = await organizations.CreateAsync("First", ownerId);
        await Assert.ThrowsAsync<OrganizationLimitReachedException>(() => organizations.CreateAsync("Second", ownerId));

        await organizations.DeleteAsync(first.Id, ownerId);

        CloudLoginOrganization second = await organizations.CreateAsync("Second", ownerId);
        Assert.Equal("Second", second.Name);
    }

    [Fact]
    public async Task UpdateAsync_saves_the_full_billing_profile_and_trims_blanks()
    {
        InMemoryCloudLoginAccountStore store = new();
        OrganizationRegistry organizations = new(store);
        Guid ownerId = Guid.NewGuid();

        CloudLoginOrganization organization = await organizations.CreateAsync("Cedar Labs", ownerId);
        organization.LegalName = "  Cedar Labs SARL  ";
        organization.TaxId = "   ";
        organization.Website = " https://cedar.example ";
        organization.BillingAddress = new OrganizationAddress { Line1 = "  1 Cedar Street ", City = " Beirut ", Country = "  " };

        CloudLoginOrganization saved = await organizations.UpdateAsync(organization, ownerId);

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
        => new() { Organization = new OrganizationConfiguration { MaxOwnedPerUser = maxOwned, MaxPerUser = maxTotal } };

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
