using AngryMonkey.Cloud.Components;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using static AngryMonkey.Cloud.Login.DataContract.User;
using Azure.Core;
using Azure;

namespace AngryMonkey.Cloud.Login
{
	public partial class CloudLogin
	{
		[Parameter] public string Value { get; set; }
		internal CosmosMethods Cosmos { get; set; }
		List<Provider> Providers { get; set; } = new();

		protected bool EnableEmailAddressField
		{
			get
			{
				return Step != ProcessStep.PendingProviders;
			}
		}

		protected ProcessStep Step { get; set; } = ProcessStep.PendingEmail;

		protected enum ProcessStep
		{
			PendingEmail,
			PendingProviders,
			PendingAuthorization
		}

		protected override async Task OnAfterRenderAsync(bool firstRender)
		{
			if (firstRender)
				Cosmos = new CosmosMethods(cloudLogin.Options.Cosmos.ConnectionString, cloudLogin.Options.Cosmos.DatabaseId, cloudLogin.Options.Cosmos.ContainerId);
		}

		private async Task OnNextClicked(MouseEventArgs e)
		{
			if (string.IsNullOrEmpty(Value))
				return;

			Step = ProcessStep.PendingProviders;

			Value = Value.ToLower();

			User? user = await Cosmos.GetUserByEmailAddress(Value);

			if (user != null)
				Providers = user.Providers.Select(key => new Provider(key)).ToList();
			else Providers = cloudLogin.Options.Providers.Select(key => new Provider(key.Code)).ToList();

			Step = ProcessStep.PendingAuthorization;
		}

		private void OnProviderClicked(Provider provider)
		{
			NavigationManager.NavigateTo($"/cloudlogin/login/{provider.Code}?emailaddress={Value}&redirectUri=/", true);
		}

		public class Provider
		{
			public Provider(string code)
			{
				Code = code.ToLower();
			}

			public string Code { get; private set; }
			//public string LoginUrl => $"cloudlogin/login?redirectUri=/&identity={Code}";
			public string? Name => Code switch
			{
				"microsoft" => "Microsoft",
				"google" => "Google",
				_ => Code
			};
		}
	}
}
