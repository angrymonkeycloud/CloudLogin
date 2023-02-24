# Cloud Login Integration Guide

Cloud Login is a login system designed for easy integration with your. NET project website. Follow the steps below to integrate Cloud Login into your project.

## Packages

There are 3 nuget packages for cloud login to work, each should be included in the right way for it to work, and a service client library for shared login between multiple websites:

    AngryMonkey.CloudLogin
    AngryMonkey.CloudLogin.Server
    AngryMonkey.CloudLogin.Shared
    AngryMonkey.CloudLogin.ServiceClient

## Blazor Webassembly Hosted Integration

We will start this by the integrating cloud login to Blazor Webassembly Hosted Server

1. Add the Cloud Login packages to your projects references.

    A. To the WebAssemply Project:
    ```csharp
    AngryMonkey.CloudLogin
    AngryMonkey.CloudLogin.Shared
    ```
    
    B. To the BlazorServer Project:
    ```csharp
    AngryMonkey.CloudLogin.Server
    ```

2. Add the Cloud Login login component to the page you want, .razor page. 

    ```csharp
    @inject CloudLoginServerClient cloudLogin

    @if (!IsAuthorized)
    {
        <AngryMonkey.CloudLogin.CloudLogin Logo="<Logo link here (.svg)>"/>
    }
    else
    {
        <a href="./cloudlogin/logout" class="--button">Logout</a>
    }
    ```
3. Add to the imports file, this line:
    ```csharp
    @using AngryMonkey.CloudLogin;
    ```
4. Add the Cloud Login login functionalities to the razor.cs of the page.

    ```csharp
    using AngryMonkey.CloudLogin.DataContract; // in the start of the file
    
    public CloudUser CurrentUser { get; set; } = new();
    public bool IsAuthorized { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        IsAuthorized = await cloudLogin.IsAuthenticated();
        CurrentUser = await cloudLogin.CurrentUser();
    }
    ```

5. In the Blazor Server project, we should add this configuration:

    ```csharp
    CloudLoginConfiguration cloudLoginConfig = new()
    {
        <CONFIGURATION HERE>
    };

    builder.Services.AddCloudLoginServer(cloudLoginConfig);

    builder.Services.AddAuthentication(opt =>
    {
        opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    });

    builder.Services.AddOptions();
    builder.Services.AddAuthenticationCore();

    builder.Services.AddScoped<CustomAuthenticationStateProvider>();
    builder.Services.AddScoped(key => new UserController());

    .
    .CODE BELOW IN THE WebApplication  configuration
    .

    app.UseCloudLogin();
    app.UseAuthentication();
    app.UseAuthorization();

    ```
6. In the WebAssembly project, we should add this configuration:

    ```csharp
    await builder.Services.AddCloudLogin(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
    ```

## Configuration

All your preferences for your website will be put inside the CloudLoginConfiguration, and inside the app secret.

1. Database Configuration

- Cloud login can work with and without database connection, without a database, all users will be treated as guests and will not have accounts on your website.

- To add a database you should add the following code:

```csharp
Cosmos = new CosmosDatabase(builder.Configuration.GetSection("Cosmos")),
```

- For more about the configuration inside the app secret on Database configuration, please see the [Cloud Login Database Integration Guide](cosmosdatabase.md).

2. Providers Configuration

- To add Providers, you should add the following code:

```csharp
Providers = new List<ProviderConfiguration>()
{
    <Code for each Provider Here>
}
```

- Then for each provider you chose you should add:

```csharp
new <ProviderNameHere>ProviderConfiguration(builder.Configuration.GetSection("<ProviderNameHere>"))
```

- For more detailed information on provider configuration, please see the [Cloud Login Providers Integration Guide](providers.md).

3. Custom Email Configuration

- To add a custom email login, you should add an SMTP Email that will be used to send Email Codes for verification.

- To add the SMTP Email, you should add the following code:

```csharp
EmailSendCodeRequest = async (sendCode) =>
{
    SmtpClient smtpClient = new(builder.Configuration["SMTP:Host"], int.Parse(builder.Configuration["SMTP:Port"]))
    {
        EnableSsl = true,
        DeliveryMethod = SmtpDeliveryMethod.Network,
        UseDefaultCredentials = false,
        Credentials = new NetworkCredential(builder.Configuration["SMTP:Email"], builder.Configuration["SMTP:Password"])
    };
    StringBuilder mailBody = new();
    mailBody.AppendLine($"code: <b style=\"color:#202124;text-decoration:none\">{sendCode.Code}</b> <br />");
    MailMessage mailMessage = new()
    {
        From = new MailAddress(builder.Configuration["SMTP:Email"], "Cloud Login"),
        Subject = "Login Code",
        IsBodyHtml = true,
        Body = mailBody.ToString()
    };
mailMessage.To.Add(sendCode.Address);
await smtpClient.SendMailAsync(mailMessage);
}
```

- For more detailed information on Custom Email configuration, please see the [Cloud Login Custom Email Integration Guide](customemail.md).

4. Footer Links Configuration

- To add custom links on the footer of your login page, you should add the following code:

```csharp

FooterLinks = new List<Link>()
{
    <Code for each Footer Link Here>
}

```

- Then for each provider you choose, you should add:

```csharp
new Link()
{
    Title = "<Link Title>",
    Url = "<Link Url>"
}
```

4. Login Duration Configuration

To specity the login duration of the users using your login, you should add the following code:
```csharp
    LoginDuration = new TimeSpan(<Hours>,<Minutes>,<Seconds>),
```

5. Login Redirection Configuration

To spicy a page to go to when logged in, you should add the following code:
```csharp
    RedirectUri = "https://YourWebsite.com/RandomPageYouChoose"
```