using AngryMonkey.Cloud.Login;
using CloudLoginDataContract;
using LoginRequestLibrary;

namespace CoverboxApp.Login.Client.Pages;
public partial class Index
{
    public CloudUser CurrentUser { get; set; } = new();
    public bool IsAuthorized { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        CloudLoginClient cloudLoginClient = new();
        cloudLoginClient.HttpServer = cloudLogin.HttpServer;

        IsAuthorized = await cloudLoginClient.IsAuthenticated();
        CurrentUser = await cloudLoginClient.CurrentUser();

        if (IsAuthorized)
        {
            Guid requestID = await cloudLoginClient.CreateUserRequest(CurrentUser.ID);
            if (CurrentUser != null)
                nav.NavigateTo($"http://localhost:5241/login?requestId={CurrentUser.ID}");
        }
    }
}
