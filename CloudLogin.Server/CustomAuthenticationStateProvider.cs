using Microsoft.AspNetCore.Components.Authorization;

namespace AngryMonkey.Cloud.Login.Controllers
{
    public class CustomAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            throw new NotImplementedException();
        }
    }
}
