# Cloud Login Standalone Integration Guide

Cloud Login Standalone is a login system designed for easy integration with your. NET project website. Follow the steps below to integrate Cloud Login into your project.

## Packages

There are 2 nuget packages for cloud login to work, each should be included in the right way for it to work:

    AngryMonkey.CloudLogin
    AngryMonkey.CloudLogin.MVC

## Blazor Webassembly Hosted Integration

We will start this by the integrating cloud login to Blazor Webassembly Hosted Server

1. Add the Cloud Login packages to your projects references.

    A. To the WebAssemply Project:
    ```csharp
    AngryMonkey.CloudLogin
    ```
    
    B. To the BlazorServer Project:
    ```csharp
    AngryMonkey.CloudLogin.MVC
    ```

2. Add the Cloud Login login Service to the program.cs in the server of the project, and add mapping to controllers. 

    ```csharp
    using AngryMonkey.CloudLogin;

    ..
    ..
    ..

    builder.Services.AddControllersWithViews();

    await builder.Services.AddCloudLoginMVC("https://login.StandaloneWebName.app/");

    ..
    ..
    ..
    
    app.UseRouting();
    app.MapControllers();
    app.UseAuthentication();
    ```
    
3. Add the Cloud Login login Service to the program.cs in the WebAssembly of the project, and add mapping to controllers.

    ```csharp
    builder.Services.AddSingleton(CloudLoginStandaloneClient.Build(builder.HostEnvironment.BaseAddress));
    ```

4. Add to the imports file, this line:

    ```csharp
    @using AngryMonkey.CloudLogin;
    ```

5. Add the Cloud Login login functionalities to the razor file of the page.

    ```csharp
    @if (IsAuthorized && CurrentUser != null)
    {
        <div>
            <h1>Login successful</h1>
            <div>Login successful @CurrentUser.DisplayName.</div>
            <div>First Name: @CurrentUser.FirstName | Last Name: @CurrentUser.LastName</div>
            <div>Email Address: @CurrentUser.PrimaryEmailAddress.Input</div>
            <a href="./account/logout">Logout</a>
        </div>
    }
    else
    {
        <a href="Account/Login?ReturnUrl=@nav.Uri">Login</a>
    }
    ```

6. Add the Cloud Login login functionalities to the razor.cs file of the page.

    ```csharp
    public User? CurrentUser { get; set; } = new();
    public bool IsAuthorized { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        // if (await cloudLogin.IsAutomaticLogin())
        // {
        //     nav.NavigateTo("Account/Login");
        // }

        IsAuthorized = await cloudLogin.IsAuthenticated();

        if (IsAuthorized)
            CurrentUser = await cloudLogin.CurrentUser();

        StateHasChanged();
    }
    ```
7. (Optional) To add automatic login, you need to add the following to the program.cs of the server side:

    ```csharp
    app.UseCloudLoginHandler();
    ```