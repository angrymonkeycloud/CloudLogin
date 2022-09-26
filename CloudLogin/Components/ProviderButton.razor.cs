using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Web;
using System.Text.Json;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using AngryMonkey.Cloud.Login.DataContract;
using static Microsoft.Extensions.DependencyInjection.CloudLoginConfiguration;
using AngryMonkey.Cloud.Components;

namespace AngryMonkey.Cloud.Login.Components
{
	public partial class ProviderButton
	{
		[Parameter]
		public Provider Provider { get; set; }
		[Parameter]
		public bool UseDefaultColor { get; set; } = false;
	}
}