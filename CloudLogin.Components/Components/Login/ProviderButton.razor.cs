using Microsoft.AspNetCore.Components;

namespace AngryMonkey.CloudLogin.Components.Login
{
    public partial class ProviderButton
    {
        [Parameter]
        public ProviderDefinition Provider { get; set; }
        [Parameter]
        public bool UseDefaultColor { get; set; } = false;
    }
}