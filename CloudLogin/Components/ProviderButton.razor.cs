using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using AngryMonkey.Cloud.Login.DataContract;

namespace AngryMonkey.Cloud.Login.Components
{
	public partial class ProviderButton
	{
		[Parameter]
		public ProviderDefinition Provider { get; set; }
		[Parameter]
		public bool UseDefaultColor { get; set; } = false;
	}
}