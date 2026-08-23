using AngryMonkey.CloudLogin.Models;
using Microsoft.AspNetCore.Components;

namespace AngryMonkey.CloudLogin.Components.Login
{
    public partial class ProviderButton
    {
        [Parameter]
        public CloudLoginProviderDefinitionModel Provider { get; set; } = null!;
        [Parameter]
        public bool UseDefaultColor { get; set; } = false;
    }
}