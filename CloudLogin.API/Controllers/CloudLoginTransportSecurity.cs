namespace AngryMonkey.CloudLogin.API.Controllers;

internal static class CloudLoginTransportSecurity
{
    public static UserModel? ForTransport(UserModel? user)
    {
        if (user is null)
            return null;

        return user with
        {
            Inputs = [.. user.Inputs.Select(input => input with
            {
                Providers = [.. input.Providers.Select(provider => provider with
                {
                    PasswordHash = null
                })]
            })]
        };
    }

    public static UserModel? ForAnonymousDiscovery(UserModel? user)
    {
        UserModel? safe = ForTransport(user);
        if (safe is null)
            return null;

        return new UserModel
        {
            Inputs = [.. safe.Inputs.Select(input => new LoginInput
            {
                Format = input.Format,
                Providers = [.. input.Providers.Select(provider => new LoginProvider
                {
                    Code = provider.Code
                })]
            })]
        };
    }
}
