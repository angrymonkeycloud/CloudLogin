using Microsoft.AspNetCore.Components;
using AngryMonkey.CloudLogin.DataContract;

namespace AngryMonkey.CloudLogin.Components
{
    public partial class ProviderButton
	{
		[Parameter]
		public ProviderDefinition Provider { get; set; }
		[Parameter]
		public bool UseDefaultColor { get; set; } = false;
	}
}