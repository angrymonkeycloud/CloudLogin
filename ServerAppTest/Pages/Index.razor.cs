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
using Newtonsoft.Json;
using Azure.Core;
using Microsoft.AspNetCore.Mvc;

namespace ServerAppTest.Pages
{
    public partial class Index
	{
		private async Task DeleteButton() => Console.WriteLine("DELETE");

        User CurrentUser { get; set; } = new();
        bool IsAuthorized { get; set; } = false;

        protected override async Task OnInitializedAsync()
		{
            //cloudLogin.InitFromServer();test

            CurrentUser = await cloudLogin.CurrentUser(HttpContextAccessor);
            IsAuthorized = await cloudLogin.IsAuthenticated(HttpContextAccessor);


		}
	}
}