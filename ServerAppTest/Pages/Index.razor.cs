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
using Azure.Core;
using Microsoft.AspNetCore.Mvc;

namespace ServerAppTest.Pages
{
	public partial class Index
	{
		private async Task DeleteButton() => Console.WriteLine("DELETE");
		CloudUser user = new();
		bool isAuthenticated = false;

		protected override async Task OnInitializedAsync()
		{
			cloudLogin.InitFromServer();

			user = await cloudLogin.CurrentUser(HttpContextAccessor);
			isAuthenticated = await cloudLogin.IsAuthenticated(HttpContextAccessor);

			IHttpContextAccessor test = HttpContextAccessor;//.HttpContext.Request.Cookies["CloudUser"];

			Console.WriteLine(test);
		}
	}
}