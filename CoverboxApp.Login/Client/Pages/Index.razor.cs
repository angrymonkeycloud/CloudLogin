using AngryMonkey.Cloud.Login;
using AngryMonkey.Cloud.Login.DataContract;
using Newtonsoft.Json;
using System.Net.Http.Json;

namespace CoverboxApp.Login.Client.Pages;
public partial class Index
{
    public CloudUser CurrentUser { get; set; } = new();
    public bool IsAuthorized { get; set; } = false;
    private HttpClient HttpServer { get; set; }

    protected override async Task OnInitializedAsync()
    {
        IsAuthorized = await cloudLogin.IsAuthenticated();
        CurrentUser = await cloudLogin.CurrentUser();

        if (IsAuthorized)
            if (CurrentUser != null)
                nav.NavigateTo($"https://localhost:7020/login?CurrentUser={CurrentUser.ID}");
    }
}
