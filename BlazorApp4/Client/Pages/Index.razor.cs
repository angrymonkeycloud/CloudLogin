using AngryMonkey.Cloud.Login.DataContract;
using Newtonsoft.Json;
using AngryMonkey.Cloud.Login;


namespace BlazorApp4.Client.Pages
{
    public partial class Index
    {

        public CloudUser User { get; set; }
        public CloudLoginClient CloudClient { get; set; } = new()
        {
            IsAuthenticated = false
        };
        public bool Authorized { get; set; } = false;
        private async Task DeleteButton() => await cloudLogin.DeleteUser(User.ID);

        protected override async Task OnInitializedAsync()
        {
            var context = HttpContextAccessor.HttpContext;
            if (context != null)
            {
                var cookies = context.Request.Cookies;
                var loginCookie = cookies["CloudLogin"];
                var cookie = cookies["CloudUser"];
                if (String.IsNullOrEmpty(loginCookie))
                    return;
                User = JsonConvert.DeserializeObject<CloudUser>(cookie);
                CloudClient = new CloudLoginClient()
                {
                    CurrentUser = User,
                    IsAuthenticated = true
                };
                StateHasChanged();
            }
        }
    }
}