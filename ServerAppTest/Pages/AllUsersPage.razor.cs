using AngryMonkey.CloudLogin;
using User = AngryMonkey.CloudLogin.User;

namespace ServerAppTest.Pages
{
    public partial class AllUsersPage
    {

        public bool Authorized { get; set; }
        public CloudLoginClient CloudClient { get; set; }
        public User User { get; set; }
        public List<User> Users { get; set; } = new();
        protected override async Task OnInitializedAsync()
        {
            
        }
    }
}