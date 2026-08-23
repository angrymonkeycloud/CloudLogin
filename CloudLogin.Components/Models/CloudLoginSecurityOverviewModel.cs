namespace AngryMonkey.CloudLogin.Models;

public class CloudLoginSecurityOverviewModel
{
    public bool HasPassword { get; set; }
    public bool PasswordProviderConfigured { get; set; }
    public bool HasAuthenticatorApp { get; set; }
    public DateTimeOffset? AuthenticatorEnrolledOn { get; set; }
    public List<CloudLoginPasskeySummaryModel> Passkeys { get; set; } = [];
    public List<CloudLoginConnectedProviderModel> ConnectedProviders { get; set; } = [];
    public List<CloudLoginProviderDefinitionModel> AvailableProviders { get; set; } = [];
}

public static class CloudLoginSecurityOverviewModelExtensions
{
    public static CloudLoginSecurityOverviewModel ToModel(this CloudLoginSecurityOverview source) => new()
    {
        HasPassword = source.HasPassword,
        PasswordProviderConfigured = source.PasswordProviderConfigured,
        HasAuthenticatorApp = source.HasAuthenticatorApp,
        AuthenticatorEnrolledOn = source.AuthenticatorEnrolledOn,
        Passkeys = [.. source.Passkeys.Select(passkey => passkey.ToModel())],
        ConnectedProviders = [.. source.ConnectedProviders.Select(provider => provider.ToModel())],
        AvailableProviders = [.. source.AvailableProviders.Select(provider => provider.ToModel())]
    };
}
