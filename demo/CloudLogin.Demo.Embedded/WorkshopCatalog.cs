using WorkshopUI;
namespace CloudLogin.Demo.Embedded;

public static class WorkshopCatalog
{
    public static IReadOnlyDictionary<string, string> Guides { get; } = WorkshopGuides.Load(typeof(WorkshopCatalog).Assembly);
    private static WorkshopFeature Account(string id, string title, string description, string instruction) =>
        new(id, title, "Account", description, DemoCodeSamples.Account,
            ["Sign in with the Authentication example. Choose Demo Admin in Test mode to explore administration.", instruction,
             "Changes use the real account component and local demo store. Restart the host to reset demo accounts."]);
    private static WorkshopFeature Login(string id, string title, string instruction) =>
        new(id, title, "Authentication", "Try the shipped authentication component with the local in-memory demo store.", DemoCodeSamples.Login,
            [instruction, "For email verification, open the Demo inbox example to retrieve the locally captured code.", "Complete the flow and inspect the account page. No external mailbox is contacted."]);
    public static IReadOnlyList<WorkshopFeature> All { get; } =
    [
        Login("authentication", "Authentication", "Choose Test mode and select Demo Admin for an immediate signed-in session."),
        Login("password", "Password and registration", "Choose Password, register a local demo email, then sign out and sign in with that password."),
        Login("verification", "Email verification codes", "Choose Email code, enter a demo email, and request a code."),
        Login("recovery", "Password recovery", "Choose Password and Forgot password. Request and complete recovery using the Demo inbox."),
        Account("profile", "Profile and contact methods", "Edit names, locale, country, email addresses, and phone numbers.", "Open the profile and contact sections. Edit an entry, save, and verify the returned values."),
        Account("authenticator", "Authenticator and passkeys", "Try security enrollment and the platform's supported authentication methods.", "Open Security to enroll an authenticator or passkey. Platform authentication requires HTTPS and a supported browser."),
        Account("devices", "Devices and sessions", "Inspect signed-in devices and session revocation.", "Open Devices. Sign in from a second browser, then revoke that device and confirm access is rejected there."),
        Account("history", "Sign-in history", "Inspect authentication activity recorded by the service.", "Open sign-in activity after signing in with different enabled providers."),
        Account("administration", "User administration", "Explore the role-gated user directory and lifecycle actions.", "Sign in as Demo Admin, open Administration, and inspect a demo user's details and lock state."),
        Account("deletion", "Account deletion and logout", "Exercise account lifecycle and coordinated sign-out.", "Create a disposable test account, inspect deletion confirmation, then delete it. Verify that the session ends."),
        new("workspaces", "Workspaces and invitations", "Workspace", "Create workspaces, add members, assign roles, and issue invitations.", DemoCodeSamples.Workspaces,
            ["Create a workspace in the live registry below.", "Add a member, specify roles and permissions, and inspect the resulting membership.", "Invite a local sample email and inspect expiration. The local registry sends no email."]),
        new("quotas", "Workspace quotas", "Workspace", "Evaluate owned and total membership limits with the production configuration contract.",
            "WorkspaceConfiguration options = new() { MaxOwnedPerUser = 3, MaxPerUser = 10 };\nint ownedLimit = options.EffectiveMaxOwnedPerUser;\nint membershipLimit = options.EffectiveMaxPerUser;",
            ["Set owned and total membership limits in Configuration.", "Run the example. The total limit also caps ownership.", "Use zero to prevent creation and CloudWorkspaceLimits.Unlimited for unlimited membership."]),
        new("roles", "Workspace access policy", "Workspace", "Evaluate role permissions across membership and invitation states.",
            "WorkspaceAccessDocument access = new() { Kind = WorkspaceAccessKinds.Membership, State = WorkspaceAccessStates.Active, Roles = [WorkspaceRoles.Admin] };\nbool canEdit = WorkspaceRolePolicy.CanEditProfile(access);\nbool canDelete = WorkspaceRolePolicy.CanDeleteWorkspace(access);",
            ["Choose role, state, and membership kind.", "Run the policy and compare edit, manage-member, and delete permissions.", "Pending invitations and disabled memberships must never grant workspace management rights."]),
        new("configuration", "Security and appearance", "Configuration", "Validate password policy, session duration, and accent-color configuration.",
            "CloudLoginWebConfiguration options = new() { PrimaryColor = \"#137d69\", LoginDuration = TimeSpan.FromDays(14) };\noptions.Security.MinimumPasswordLength = 12;\nCloudLoginConfigurationValidator.Validate(options, isDevelopment: true);",
            ["Change the accent, minimum password length, or session duration.", "Validate to see the real configuration guardrails.", "The preview does not mutate authentication policy for other workshop users. Apply validated options when registering your application."]),
        new("redirects", "Redirect origins and handoffs", "Configuration", "Compare exact origins using CloudLogin's URL contract.",
            "bool sameOrigin = CloudLoginShared.IsSameOrigin(\"https://portal.example/account\", \"https://portal.example\");\nstring logout = CloudLoginShared.BuildLogoutUrl(\"https://login.example\", \"/Account\");",
            ["Enter a candidate URL and allowed origin.", "Run the comparison. Scheme, host, and effective port must agree.", "Register external websites with AllowWebsite and mobile apps with AllowMobileApp. Relative paths are validated separately by the authority."]),
        new("inbox", "Demo inbox", "Developer tools", "Read verification codes captured by the local demo email callback.",
            "options.EmailSendCodeRequest = value => { demoInbox.Capture(value.Address, value.Code); return Task.CompletedTask; };",
            ["Request a code from the authentication or recovery example.", "Refresh the inbox to retrieve the local code.", "This inbox and Test mode are restricted to the Development host. Never use real credentials in demo accounts."]),
        .. Guides.Select(guide => WorkshopGuides.Feature(guide.Key, guide.Value))
    ];
}
