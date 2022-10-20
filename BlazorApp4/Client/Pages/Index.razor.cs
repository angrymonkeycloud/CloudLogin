using AngryMonkey.Cloud.Login.DataContract;
using Newtonsoft.Json;
using AngryMonkey.Cloud.Login;
using Microsoft.AspNetCore.Components;


namespace BlazorApp4.Client.Pages
{
    public partial class Index
    {
        public CloudUser User { get; set; } = new();
        public bool Authorized { get; set; } = false;
        private async Task DeleteButton() => await cloudLogin.DeleteUser(User.ID);

        protected override async Task OnInitializedAsync()
        {
            Console.Write(cloudLogin.IsAuthenticated);
            if (cloudLogin.IsAuthenticated == true)
            {
                User = cloudLogin.CurrentUser;
            }

            StateHasChanged();
        }
    }
}