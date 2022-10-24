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