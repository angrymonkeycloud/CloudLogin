using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using System.Web;

namespace AngryMonkey.CloudLogin;

public partial class CloudLoginPage
{
    [Parameter] public string Logo { get; set; }
    public string redirectUri { get; set; }
    public string actionState { get; set; }
    public User CurrentUser { get; set; } = new();
    public bool IsAuthorized { get; set; } = false;
    public bool Show { get; set; } = false;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            Uri uri = nav.ToAbsoluteUri(nav.Uri);
            QueryHelpers.ParseQuery(uri.Query).TryGetValue("redirectUri", out StringValues redirectUriValue);
            QueryHelpers.ParseQuery(uri.Query).TryGetValue("actionState", out StringValues actionStateValue);

            redirectUri = redirectUriValue;
            actionState = actionStateValue;
            StateHasChanged();
        }
    }
    protected override async Task OnInitializedAsync()
    {
        IsAuthorized = await cloudLogin.IsAuthenticated();
        CurrentUser = await cloudLogin.CurrentUser();

        if (IsAuthorized && actionState == "login")
        {
            Guid requestID = await cloudLogin.CreateUserRequest(CurrentUser.ID);
            if (CurrentUser != null)
            {
                if (string.IsNullOrEmpty(redirectUri))
                    return;
                else
                    nav.NavigateTo($"{redirectUri}&requestId={requestID}");
            }
        }
        Show = true;
    }
}
