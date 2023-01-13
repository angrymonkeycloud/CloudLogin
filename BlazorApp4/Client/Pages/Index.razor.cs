using AngryMonkey.Cloud.Login.DataContract;
using Newtonsoft.Json;
using AngryMonkey.Cloud.Login;
using Microsoft.AspNetCore.Components;


namespace ServerClientDemo.Client.Pages
{
    public partial class Index
    {
        public CloudUser CurrentUser { get; set; } = new();
        public bool IsAuthorized { get; set; } = false;
        private async Task DeleteButton() => await cloudLogin.DeleteUser(CurrentUser.ID);
        private async Task CheckUsername()
        {
            await cloudLogin.GetUsersByDisplayName("rami gerges");
        }

        protected override async Task OnInitializedAsync()
        {
            IsAuthorized = await cloudLogin.IsAuthenticated();
            CurrentUser = await cloudLogin.CurrentUser();
        }
        private string? ImportedPhoneNumber { get; set; }
        private string? ImportedCountryCode { get; set; }
        private string? ImportedCallingCode { get; set; }

        private async Task ImportNumber()
        {
            cloudLogin.AddPhoneNumber(CurrentUser.ID, ImportedPhoneNumber, ImportedCountryCode, ImportedCallingCode);
        }
    }
}