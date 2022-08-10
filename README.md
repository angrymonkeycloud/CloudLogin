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
