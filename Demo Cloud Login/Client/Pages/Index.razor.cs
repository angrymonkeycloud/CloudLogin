using AngryMonkey.CloudLogin.DataContract;

namespace Demo_Cloud_Login.Client.Pages;
public partial class Index
{
    public CloudUser? CurrentUser { get; set; }
    public bool IsAuthorized { get; set; }

    protected override async Task OnInitializedAsync()
    {
        IsAuthorized = await cloudLogin.IsAuthenticated();
        CurrentUser = await cloudLogin.CurrentUser();
    }

}
