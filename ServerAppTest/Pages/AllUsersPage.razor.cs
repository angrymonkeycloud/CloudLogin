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
using AngryMonkey.CloudLogin.DataContract;
using AngryMonkey.CloudLogin;
using Newtonsoft.Json;
using System.Net;

namespace ServerAppTest.Pages
{
    public partial class AllUsersPage
    {

        public bool Authorized { get; set; }
        public CloudLoginServerClient CloudClient { get; set; }
        public User User { get; set; }
        public List<User> Users { get; set; } = new();
        protected override async Task OnInitializedAsync()
        {
            
        }
    }
}