# Cloud Login Providers Integration Guide

## Main provider configuration
To add Providers, you should add the following code:
```csharp
Providers = new List<ProviderConfiguration>()
{
    <Code for each provider here>
}
```
Inside ProviderConfiguration, you can use the following for each of the providers that we support:

1. Microsoft:
    ```csharp
    new MicrosoftProviderConfiguration(builder.Configuration.GetSection("Microsoft"))
    ```
    
2. Google:
    ```csharp
    new GoogleProviderConfiguration(builder.Configuration.GetSection("Google"))
    ```
    
3. Facebook:
    ```csharp
    new FacebookProviderConfiguration(builder.Configuration.GetSection("Facebook"))
    ```
    
4. Twitter:
    ```csharp
    new TwitterProviderConfiguration(builder.Configuration.GetSection("Twitter"))
    ```
    
5. WhatsApp:
    ```csharp
    new WhatsAppProviderConfiguration(builder.Configuration.GetSection("WhatsApp"))
    ```

6. Custom Email:
    ```csharp
    new CustomProviderConfiguration(builder.Configuration.GetSection("Custom"))
    ```
## inside app secret:
For each provider added above you should add the configuration of it inside the secret, For example:
1. Microsoft:
    ```csharp
    "Microsoft": {
        "ClientId": "<Microsoft Client ID>",
        "ClientSecret": "<Microsoft Secret>"
    }
    ```
    
2. Google:
    ```csharp
    "Google": {
        "ClientId": "<Google Client ID>",
        "ClientSecret": "<Google Secret>"
    }
    ```
    
3. Facebook:
    ```csharp
    "Facebook": {
        "ClientId": "<Facebook Client ID>",
        "ClientSecret": "<Facebook Secret>"
    },
    ```
    
4. Twitter:
    ```csharp
    "Twitter": {
        "ClientId": "<Twitter Cliend ID>",
        "ClientSecret": "<Twitter Secret>"
    }
    ```
    
5. WhatsApp:
    ```csharp
    "WhatsApp": {
        "RequestUri": "<WhatsApp Request Uri>",
        "Authorization": "<WhatsAppAuthorication Code>",
        "Template": "<WhatsApp Template for code sending>",
        "Language": "<WhatsApp Template language>"
    }
    ```

6. Custom Email:
    ```csharp,
    "Custom": {
        "Label": "<Label Name>",
    }
    ```
For each provider there is a different procedure to get client id and secret, please see the [Add Identity Provider Guide](https://learn.microsoft.com/en-us/azure/active-directory-b2c/add-identity-provider)

## Shared login update and new functionalities

Recently, we added a shared login configuration that lets you connect multiple websites into a single login system, and we added the ability to allow users to edit their profiles, adding multiple emails to a single account and the ability to switch between primary emails.

If you want any of the providers to be used only for the purpose of adding it to an already existing account, and not the ability to let it be the primary email of the account, you can make True the HandleUpdateOnly inside the provider configuration, for example:

```csharp
new CustomProviderConfiguration(builder.Configuration.GetSection("Custom"), true)
```

## Upcoming functionality we're working on

For future functions, we will be working on a controller that can handle all user updates, user data and user interaction in cloud login.