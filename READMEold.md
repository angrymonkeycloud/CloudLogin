# ----------------------------------------OLD
# Cloud Login

An MVC project that shows how to add AngryMonkey Cloud Login to your website with your own sign in/out page, and lets you connect to different social identity providers.

## Initialization

### Blazor Server-Side Application Configuration

Program.cs:
```csharp 

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

CloudLoginConfiguration cloudLoginConfig = new()
{
    ..<CONFIGURATION HERE>..
}


builder.Services.AddCloudLoginServer(cloudLoginConfig);

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});

await builder.Services.AddCloudLogin();


builder.Services.AddHttpContextAccessor();


builder.Services.AddOptions();
builder.Services.AddAuthenticationCore();

builder.Services.AddScoped<CustomAuthenticationStateProvider>();
builder.Services.AddScoped(key => new UserController());

builder.Services.AddScoped<CustomAuthenticationStateProvider>();
builder.Services.AddScoped(key => new UserController());

.
.CODE BELOW IN THE WebApplication  configuration
.

app.UseCloudLogin();
app.UseAuthentication();
app.UseAuthorization();

```

index.razor.cs
```csharp
CloudUser CurrentUser { get; set; } = new();
bool IsAuthorized { get; set; } = false;
protected override async Task OnInitializedAsync()
{
    CurrentUser = await cloudLogin.CurrentUser(HttpContextAccessor);
    IsAuthorized = await cloudLogin.IsAuthenticated(HttpContextAccessor);
}
```


### Blazor Server-Client-Side Application Configuration

Program.cs Server-side:
```csharp

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

CloudLoginConfiguration cloudLoginConfig = new()
{
    ..<CONFIGURATION HERE>..
}

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
Program.cs Client-side:
```csharp
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Services.AddCloudLogin(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });


builder.Services.AddApiAuthorization();

builder.Services.AddHttpContextAccessor();
```
index.razor.cs
```csharp
        CloudUser CurrentUser { get; set; } = new();
        bool IsAuthorized { get; set; } = false;
        

        protected override async Task OnInitializedAsync()
        {
            IsAuthorized = await cloudLogin.IsAuthenticated();
            CurrentUser = await cloudLogin.CurrentUser();
        }
```

### For the Configation code inside cloudLoginConfig

#### For Cloud Login with online database and user handling, you should subscribe to Azure Cosmos and create a database and a container:

```csharp
 Cosmos = new CosmosDatabase()
    {
        ConnectionString = /*Connection String here*/,
        DatabaseId = /*Create a database and but the ID here*/,
        ContainerId = /*Create a Container and put its ID here*/
    },
``` 

#### Adding your own footer links:

```csharp
    FooterLinks = new List<Link>()
    {
        new Link()
        {
            Title = /*Title for your link*/,
            Url = /*Link URL*/
        }
    }
```

#### For Email verification you should have an Email SMTP setup, ex from google:
 https://www.gmass.co/blog/gmail-smtp/

```csharp
    EmailSendCodeRequest = async (sendCode) =>
    {
        SmtpClient smtpClient = new(/*Your Email Host*/, int.Parse(/*Your Email Port*/))
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(/*Your Email*/, /*Your Password*/)
        };

        StringBuilder mailBody = new();
        /*Any paragraph you want to write example below*/
        mailBody.AppendLine("<h3>Hello,</h3>");
        mailBody.AppendLine($"Your Code: <b style=\"color:#202124;text-decoration:none\">{sendCode.Code}</b> <br />");

        MailMessage mailMessage = new()
        {
            From = new MailAddress(/*Your Email*/, "Cloud Login"),
            Subject = /*Email Subject*/,
            IsBodyHtml = true,
            Body = mailBody.ToString()
        };

        mailMessage.To.Add(sendCode.Address);

        await smtpClient.SendMailAsync(mailMessage);
    }
```

#### For adding multiple providers
For each provider there is a different procedur to get client id and secret, use this link for help
https://learn.microsoft.com/en-us/azure/active-directory-b2c/add-identity-provider

```csharp
Providers = new List<ProviderConfiguration>()
    {
        //Microsoft Setup
        new MicrosoftProviderConfiguration()
        {
            ClientId = /*Client ID*/,
            ClientSecret= /*Client Secret*/,
        },
        //Google Setup
        new GoogleProviderConfiguration()
        {
            ClientId = /*Client ID*/,
            ClientSecret= /*Client Secret*/,
        },
        //Facebook Setup
        new FacebookProviderConfiguration()
        {
            ClientId = /*Client ID*/,
            ClientSecret= /*Client Secret*/,
        },
        //Twitter Setup
        new TwitterProviderConfiguration()
        {
            ClientId = /*Client ID*/,
            ClientSecret= /*Client Secret*/,
        },
        //WhatsApp Setup
        new WhatsAppProviderConfiguration()
        {
            RequestUri = /*Whatsapp Request URI*/,
            Authorization = /*Whatsapp Authorization Bearer*/,
            Template = /*Whatsapp Template*/,
            Language = /*Whatsapp Template Language*/
        }
    }
```

#### Redirect URL if you want when the login finish to redirect to a custom link/page
Leave empty to stay on the same page
```csharp
redirectUri  = /*Redirect Link*/
```

### lastly you have to make an authorized and not authorized state and get the signed in user id , display name etc..
index.razor
```csharp
@inject AngryMonkey.Cloud.Login.CloudLoginClient cloudLogin
@inject IHttpContextAccessor HttpContextAccessor
//Injection at the top of the page


@if (IsAuthorized == false)
{
    //NOT authorized

    <AngryMonkey.Cloud.Login.CloudLogin Logo="<YOUR LOGO LINK>" />
}
else
{
    //Authorized
    @CurrentUser.ID
    @CurrentUser.DisplayName
    @CurrentUser.Inputs.Where(key => key.IsPrimary == true).FirstOrDefault().Input
    etc..
}
```


### Recommendation

All IDs, Secrets, Passwords, etc.. to be kept in a secrets file to not make your website vulnerable.

## Contribution

For TypeScript compilation please install Cloud Mate from npm

```batch
npm i -g cloudmate
```

To generate testing JavaScript file and keep watching for changes run the below:

```batch
cloudmate -w
```

When you're done, pleae update the version under package.json and run the following for generating distribution files:

```batch
cloudmate dist
```

Check out <https://angrymonkeycloud.com/cloudlocalization> for more information.

# ----------------------------------------OLDER

# Cloud Login

An MVC project that shows how to add Azure AD B2C To your website with cutom sign in/out page, and lets you connect to different social identity providers.

## Contribution

For TypeScript compilation please install Cloud Mate from npm

```batch
npm i -g cloudmate
```

To generate testing JavaScript file and keep watching for changes run the below:

```batch
cloudmate -w
```

When you're done, pleae update the version under package.json and run the following for generating distribution files:

```batch
cloudmate dist
```

Check out <https://angrymonkeycloud.com/cloudlocalization> for more information.

## How to use our custom page for your Login page:

In your B2C_1_signin-policy goto Customize : Page layouts, in Use custom page content put yes and put our custom page uri: https://amadevelopers.blob.core.windows.net/azuretestlogin/loginpage.html


## How to create an app in your Azure Portal:

### You should create a new directory, app registration and start with Initializing SignUpSignInPolicy and PasswordPolicy:

1. Creating a new App registration
    1. In your newly created directory, go to Azure AD B2C ->  Manage : App registrations -> App registration
    2. Register an application
        1. Name : your application display name
        2. Supported account types : Default
        3. Redirect URI (recommended): Web -> https://localhost:PORT/signin-oidc
        4. Permissions : Default
    3. Create

2. Initializing SignUpSignInPolicy
    1. In your newly created directory, go to Azure AD B2C ->  Policies : User Flows -> New user flow
    2. Select a user flow type : "Sign up and sign in" and Select a Version : Standard (Legacy) -> Create
    3. Create
        1. Name B2C_1_yourSigninPolicyName
        2. Identity providers Local :  Email signup
        3. Social identity providers : Microsoft + Google
        4. Multifactor authentication -> Disabled
        6. User attributes and token claims:
            1. Collect attributes
                1. Country/Region
                2. Display Name
                3. Email Address
                4. Given Name
                5. Surname
            2. Return claim
                1. Country/Region
                2. Display Name
                3. Email Addresses
                4. Given Name
                5. Identity Provider
                6. Identity Provider Access Token
                7. Surname
                8. User's Object ID
    4. Create

3. Initializing PasswordPolicy
    1. In your newly created directory, go to Azure AD B2C ->  Policies : User Flows -> New user flow
    2. Select a user flow type : "Password reset" and Select a Version : Standard (Legacy) -> Create
    3. Create
        1. Name B2C_1_YourPasswordPolicy
        2. Identity providers : Reset password using email address
        3. Multifactor authentication -> Disabled
    4. Create