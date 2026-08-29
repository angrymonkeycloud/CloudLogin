namespace CloudLogin.Demo.Embedded;

public sealed record DemoFeatureDefinition(string Title, string Area, string Description, string Route, string Badge, string[] Capabilities);

public static class DemoFeatureCatalog
{
    public static IReadOnlyList<DemoFeatureDefinition> All { get; } =
    [
        new("Authentication", "Sign-in journey", "Run the shipped CloudLogin login component with test mode, email/password, and email/code providers.", "/login", "Live component", ["Login", "Registration", "Password recovery", "Logout"]),
        new("Provider studio", "Authentication methods", "Understand which providers are enabled, what each one tests, and where the email verification code appears.", "/providers", "3 providers", ["Test mode", "Password", "Email code"]),
        new("Account center", "Profile management", "Use the real account UI for user information, profile editing, email addresses, phone numbers, logout, and account deletion.", "/account", "Live component", ["Profile", "Emails", "Phone numbers", "Delete"]),
        new("Administration", "Global admin", "Sign in as Demo Admin to unlock the real administration tab, user directory, user details, and account controls.", "/account", "Role gated", ["User list", "User details", "Global admin"]),
        new("Workspaces", "Business identity", "Create businesses, assign owners, add team members, attach roles and permissions, and issue invitations.", "/workspaces", "Interactive", ["Owners", "Members", "Roles", "Permissions", "Invitations"]),
        new("Demo inbox", "Email verification", "Inspect one-time codes captured by the local email/code provider without configuring SMTP.", "/inbox", "Local only", ["Verification codes", "Password recovery"])
    ];
}
