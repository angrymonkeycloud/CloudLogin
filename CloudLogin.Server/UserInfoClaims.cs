using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace AngryMonkey.CloudLogin
{
    public class UserInfoClaims : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            return Task.FromResult(principal);
        }
    }
}
