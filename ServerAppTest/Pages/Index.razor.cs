using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using ServerAppTest;
using ServerAppTest.Shared;
using AngryMonkey.Cloud.Components;
using Microsoft.Azure.Cosmos;
using System.Security.Claims;
using AngryMonkey.Cloud.Login.DataContract;
using Newtonsoft.Json;
using AngryMonkey.Cloud.Login;

namespace ServerAppTest.Pages
{
    public partial class Index
    {
        public CloudUser User { get; set; }
        public CloudLoginClient CloudClient { get; set; }
        public bool Authorized { get; set; }
        private async Task DeleteButton() => await cloudLogin.DeleteUser(User.ID);
        protected override async Task OnInitializedAsync()
        {
            var context = HttpContextAccessor.HttpContext;
            if (context != null)
            {
                var cookies = context.Request.Cookies;
                var loginCookie = cookies["CloudLogin"];
                var cookie = cookies["CloudUser"];
                if (String.IsNullOrEmpty(loginCookie))
                    return;
                User = JsonConvert.DeserializeObject<CloudUser>(cookie);
                CloudClient = new CloudLoginClient()
                {
                    CurrentUser = User,
                    IsAuthenticated = true
                };
            }
        }
    }
}