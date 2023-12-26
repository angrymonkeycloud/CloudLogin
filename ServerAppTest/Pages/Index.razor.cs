using AngryMonkey.CloudLogin;

namespace ServerAppTest.Pages
{
    public partial class Index
	{
		private async Task DeleteButton() => Console.WriteLine("DELETE");

        User CurrentUser { get; set; }
        bool IsAuthorized { get; set; } = false;

        protected override async Task OnInitializedAsync()
		{
            //cloudLogin.InitFromServer();test

            CurrentUser = await cloudLogin.CurrentUser();
            IsAuthorized = await cloudLogin.IsAuthenticated();

            Console.WriteLine(cloudLogin.HttpServer.BaseAddress);
        }
	}
}