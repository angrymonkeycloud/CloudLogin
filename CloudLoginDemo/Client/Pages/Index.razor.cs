using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;
using CloudLoginDemo.Client;
using CloudLoginDemo.Client.Shared;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CloudLoginDemo.Client.Pages
{
    public partial class Index
    {
        private string authMessage;
        private string surnameMessage;
        private IEnumerable<Claim> claims = Enumerable.Empty<Claim>();

        protected override async Task OnInitializedAsync()
        {
			await base.OnInitializedAsync();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
		{
			await GetClaimsPrincipalData();

			await base.OnAfterRenderAsync(firstRender);
        }

        private async Task GetClaimsPrincipalData()
        {
            var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
			var user = authState.User;

            if (user.Identity.IsAuthenticated)
            {
                authMessage = $"{user.Identity.Name} is authenticated.";
                claims = user.Claims;
                surnameMessage = $"Surname: {user.FindFirst(c => c.Type == ClaimTypes.Surname)?.Value}";
            }
            else
            {
                authMessage = "The user is NOT authenticated.";
            }
        }
    }
}