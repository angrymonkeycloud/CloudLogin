using AngryMonkey.CloudLogin.DataContract;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace SharedLogin.WebAssembly.Pages;

public partial class Index
{
    public string domainName { get; set; }
    public string actionState { get; set; }
    public CloudUser CurrentUser { get; set; } = new();
    public bool IsAuthorized { get; set; } = false;
    public bool Show { get; set; } = false;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            Uri uri = nav.ToAbsoluteUri(nav.Uri);
            QueryHelpers.ParseQuery(uri.Query).TryGetValue("domainName", out StringValues domainNameValue);
            QueryHelpers.ParseQuery(uri.Query).TryGetValue("actionState", out StringValues actionStateValue);

            domainName = domainNameValue;
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
                if (string.IsNullOrEmpty(domainName))
                    return;
                else
                    nav.NavigateTo($"{domainName}/login?requestId={requestID}");
            }
        }
        Show = true;
    }
}
