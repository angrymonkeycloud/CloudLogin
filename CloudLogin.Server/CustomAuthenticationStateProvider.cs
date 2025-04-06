using Microsoft.AspNetCore.Components.Authorization;

namespace AngryMonkey.CloudLogin;
public class CustomAuthenticationStateProvider : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        throw new NotImplementedException();
    }
}