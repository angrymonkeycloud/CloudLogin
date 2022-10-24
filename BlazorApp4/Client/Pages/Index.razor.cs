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

        protected override async Task OnInitializedAsync()
        {
            IsAuthorized = await cloudLogin.IsAuthenticated();
            CurrentUser = await cloudLogin.CurrentUser();
        }
    }
}