using System.Text.Json;
using AngryMonkey.CloudLogin;
using AngryMonkey.CloudLogin.Interfaces;

namespace CloudLogin.Demo;

public sealed class DemoAccountRegistrySeed(IServiceScopeFactory scopeFactory, Guid ownerUserId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// The demo admin's user id, so the seeded workspaces
    /// belong to the account you sign in as — the account page shows them straight away
    /// instead of an empty workspace.
    /// </summary>
    public Guid OwnerUserId { get; } = ownerUserId;
    public IReadOnlyList<CloudWorkspace> Workspaces { get; private set; } = [];
    public IReadOnlyList<CloudWorkspaceInvitation> Invitations { get; private set; } = [];

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _gate.WaitAsync();
        try
        {
            if (_initialized)
                return;

            using IServiceScope scope = scopeFactory.CreateScope();
            ICloudLoginWorkspaceRegistry workspaces = scope.ServiceProvider.GetRequiredService<ICloudLoginWorkspaceRegistry>();
            ICloudLoginAccountStore accounts = scope.ServiceProvider.GetRequiredService<ICloudLoginAccountStore>();

            CloudWorkspace cedarLabs = await workspaces.CreateAsync("Cedar Labs", OwnerUserId);
            CloudWorkspace northstarClinic = await workspaces.CreateAsync("Northstar Clinic", OwnerUserId);

            // Filled in so the workspace workspace has real information and billing details
            // to show, the way a set-up workspace would.
            cedarLabs.LegalName = "Cedar Labs SARL";
            cedarLabs.AdditionalBillingInformation = "Attn: Rita Haddad, Finance";
            cedarLabs.BillingEmail = "billing@cedarlabs.example";
            cedarLabs.BillingPhone = "+961 1 000 000";
            cedarLabs.Website = "https://cedarlabs.example";
            cedarLabs.TaxNumber = "LB-1234567";
            cedarLabs.BillingAddress = new CloudWorkspaceAddress
            {
                Line1 = "12 Cedar Street",
                Line2 = "4th floor",
                City = "Beirut",
                PostalCode = "1103",
                Country = "Lebanon"
            };
            cedarLabs = await workspaces.UpdateAsync(cedarLabs, OwnerUserId);

            northstarClinic.BillingEmail = "accounts@northstar.example";
            northstarClinic.AdditionalBillingInformation = "Attn: Dr. Karim Nasr";
            northstarClinic.BillingAddress = new CloudWorkspaceAddress { Line1 = "88 West Bay", City = "Doha", Country = "Qatar" };
            northstarClinic = await workspaces.UpdateAsync(northstarClinic, OwnerUserId);

            Workspaces = [cedarLabs, northstarClinic];

            await AddMemberAsync(accounts, workspaces, cedarLabs.Id, ["BillingAdmin", "Developer"], ["billing.manage", "members.read"]);
            await AddMemberAsync(accounts, workspaces, cedarLabs.Id, ["Support"], ["members.read", "invitations.create"]);
            await AddMemberAsync(accounts, workspaces, northstarClinic.Id, ["Scheduler"], ["appointments.manage", "members.read"]);

            CloudWorkspaceInvitation cedarInvitation = await workspaces.InviteAsync(cedarLabs.Id, "partner@example.invalid", OwnerUserId, DateTimeOffset.UtcNow.AddDays(7), ["Developer"]);
            CloudWorkspaceInvitation clinicInvitation = await workspaces.InviteAsync(northstarClinic.Id, "doctor@example.invalid", OwnerUserId, DateTimeOffset.UtcNow.AddDays(3), ["Practitioner"]);
            Invitations = [cedarInvitation, clinicInvitation];

            // Expired and under the default policy, so the account page offers to remove it.
            // Long expired but marked Never, to show an entry the account holder can't clear and
            // that keeps its workspace from being deleted.
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task AddMemberAsync(ICloudLoginAccountStore accounts, ICloudLoginWorkspaceRegistry workspaces, Guid workspaceId, IReadOnlyList<string> roles, IReadOnlyList<string> permissions)
    {
        Guid userId = Guid.NewGuid();
        await workspaces.AddMemberAsync(workspaceId, userId, roles);
        await accounts.SaveMemberAsync(new()
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Roles = roles,
            Permissions = permissions
        });
    }
}
