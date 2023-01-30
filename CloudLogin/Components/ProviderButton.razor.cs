using Microsoft.AspNetCore.Components;
using CloudLoginDataContract;

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