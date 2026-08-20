using System.Text.Json;
using AngryMonkey.CloudLogin.Interfaces;
using AngryMonkey.CloudLogin.Server.Services;

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
