using AngryMonkey.CloudLogin.DataContract;
using Microsoft.AspNetCore.Components;

namespace SharedLogin.WebAssembly.Pages;

public partial class Index
{
    [Parameter] public string domainName { get;set; }
    public CloudUser CurrentUser { get; set; } = new();
    public bool IsAuthorized { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        IsAuthorized = await cloudLogin.IsAuthenticated();
        CurrentUser = await cloudLogin.CurrentUser();

        if (IsAuthorized)
        {
            Guid requestID = await cloudLogin.CreateUserRequest(CurrentUser.ID);
            if (CurrentUser != null)
                nav.NavigateTo($"{domainName}/login?requestId={requestID}");
        }
    }
}
