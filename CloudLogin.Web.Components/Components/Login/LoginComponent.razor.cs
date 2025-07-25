using AngryMonkey.CloudLogin.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AngryMonkey.CloudLogin;

public partial class LoginComponent
{
    //GENERAL VARIABLES--------------------------------------
    [Parameter] public string? Logo { get; set; }
    [Parameter] public bool Embedded { get; set; } = false;
    [Parameter] public string? ActionState { get; set; }
    [Parameter] public required User CurrentUser { get; set; }
    public Guid UserId { get; set; } = Guid.NewGuid();
    [Parameter] public string? RedirectUri { get; set; }
    private string RedirectUriValue => RedirectUri ?? cloudLogin.RedirectUri ?? navigationManager.Uri;
    private string Email { get; set; } = string.Empty;
    private string Password { get; set; } = string.Empty;
    private string ConfirmPassword { get; set; } = string.Empty;

    //INPUT VARIABLES----------------------------------------
    public string PrimaryEmail { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool KeepMeSignedIn { get; set; }
    public bool AddInputDiplay { get; set; } = false;
    private string _inputValue = string.Empty;
    public string InputValue
    {
        get => _inputValue;
        set
        {
            if (value == _inputValue)
                return;

            _inputValue = value;

            OnInput.Invoke();
        }
    }
    List<InputFormat> AvailableFormats
    {
        get
        {
            List<InputFormat> formats = [];

            if (EmailAddressEnabled)
                formats.Add(InputFormat.EmailAddress);

            if (PhoneNumberEnabled)
                formats.Add(InputFormat.PhoneNumber);

            return formats;
        }
    }
    protected InputFormat InputValueFormat => cloudLogin.GetInputFormat(InputValue);

    protected string CssClass
    {
        get
        {
            List<string> classes = [];

            if (Auth.IsLoading)
                classes.Add("_loading");

            return string.Join(" ", classes);
        }
    }

    //PROVIDERS VARIABLES------------------------------------
    List<ProviderDefinition> Providers { get; set; } = [];
    List<ProviderDefinition> ExternalProviders => Providers.Where(key => key.IsExternal).ToList();
    public bool EmailAddressEnabled => Providers.Any(key => key.HandlesEmailAddress);
    public bool PhoneNumberEnabled => Providers.Any(key => key.HandlesPhoneNumber);
    public ProviderDefinition? SelectedProvider { get; set; }
    public Action OnInput { get; set; } = () => { };

    protected bool Next { get; set; } = false;
    protected bool Preview { get; set; } = false;

    //VERIFICATION VARIABLES---------------------------------
    public string VerificationValue { get; set; } = string.Empty;
    public bool ExpiredCode { get; set; } = false;
    public string? VerificationCode { get; set; }
    public DateTimeOffset? VerificationCodeExpiry { get; set; }

    //REGISTRATION VARIABLES----------------------------------
    public List<ProviderDefinition> NonExternalProviders => Providers.Where(key => !key.IsExternal).ToList();
    public List<ProviderDefinition> AvailableRegistrationProviders => NonExternalProviders.Where(p =>
        p.Code.Equals("code", StringComparison.OrdinalIgnoreCase) ||
        p.Code.Equals("password", StringComparison.OrdinalIgnoreCase)).ToList();
    public bool HasCodeProvider => AvailableRegistrationProviders.Any(p => p.Code.Equals("code", StringComparison.OrdinalIgnoreCase));
    public bool HasPasswordProvider => AvailableRegistrationProviders.Any(p => p.Code.Equals("password", StringComparison.OrdinalIgnoreCase));
    public string? SelectedRegistrationMethod { get; set; }

    //START FUNCTIONS----------------------------------------
    protected override async Task OnInitializedAsync()
    {
        if (await cloudLogin.IsAuthenticated())
        {
            navigationManager.NavigateTo("/", true);
            return;
        }

        if (string.IsNullOrEmpty(ActionState))
            ActionState = "login";

        Auth.OnStateChanged += StateHasChanged;

        Providers = await cloudLogin.GetProviders();

        if (!string.IsNullOrEmpty(ActionState))
        {
            switch (ActionState)
            {
                case "UpdateInput":
                    Auth.StartLoading();

                    if (CurrentUser == null)
                        return;

                    LoginInput? primaryInput = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true);
                    _inputValue = primaryInput?.Input ?? string.Empty;
                    FirstName = CurrentUser.FirstName ?? string.Empty;
                    LastName = CurrentUser.LastName ?? string.Empty;
                    DisplayName = CurrentUser.DisplayName ?? string.Empty;
                    UserId = CurrentUser.ID;

                    await Auth.SwitchStep(ProcessStep.Registration);
                    break;

                case "AddInput":
                    LoginInput? primaryInputForAdd = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true);
                    PrimaryEmail = primaryInputForAdd?.Input ?? string.Empty;
                    break;

                case "AddNumber":
                    LoginInput? primaryInputForNumber = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true);
                    PrimaryEmail = primaryInputForNumber?.Input ?? string.Empty;
                    break;

                case "AddEmail":
                    LoginInput? primaryInputForEmail = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true);
                    PrimaryEmail = primaryInputForEmail?.Input ?? string.Empty;
                    break;

                default:
                    break;
            }
        }

        OnInput = StateHasChanged;

        await Auth.SwitchStep(ProcessStep.InputValue);

        await base.OnInitializedAsync();
    }

    //BUTTONS CLICKED FUNCTIONS------------------------------
    private async Task OnInputNextClicked()
    {
        Auth.Errors.Clear();

        if (string.IsNullOrEmpty(InputValue))
            return;

        Auth.StartLoading();

        InputValue = InputValue.ToLower();

        User? user = await cloudLogin.GetUserByInput(InputValue);

        if (user != null)
        {
            // Existing user - proceed with login flow
            UserId = user.ID;

            Auth.Input = new SelectedInput(InputValue.ToLower());
            Auth.Input.IsFound = true;

            foreach (string providerCode in user.Providers)
            {
                ProviderDefinition? provider = Providers.FirstOrDefault(p => p.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase));

                if (provider != null)
                    Auth.Input.Providers.Add(provider);
            }

            if (Auth.Input.Providers.Count == 0)
                Auth.Input.Providers = [.. Providers];

            await Auth.SwitchStep(ProcessStep.Providers);
        }
        else
        {
            // New user - check if we should proceed with registration
            if (NonExternalProviders.Count > 0)
            {
                // We have internal providers (Code/Password), proceed with registration
                Auth.Input = new SelectedInput(InputValue.ToLower());
                Auth.Input.IsFound = false;

                await Auth.SwitchStep(ProcessStep.RegistrationDetails);
            }
            else
            {
                Auth.Errors.Add("Email address not found.");
            }
        }

        Auth.EndLoading();
    }

    private async Task OnRegistrationInputNextClicked()
    {
        Auth.Errors.Clear();

        if (string.IsNullOrEmpty(InputValue))
            return;

        if (InputValueFormat != InputFormat.EmailAddress && InputValueFormat != InputFormat.PhoneNumber)
        {
            Auth.Errors.Add("Please enter a valid email address or phone number.");
            return;
        }

        Auth.StartLoading();

        InputValue = InputValue.ToLower();

        User? user = await cloudLogin.GetUserByInput(InputValue);

        if (user != null)
        {
            Auth.Errors.Add("An account with this email/phone already exists. Please sign in instead.");
            Auth.EndLoading();
            return;
        }

        // Proceed with registration
        Auth.Input = new SelectedInput(InputValue.ToLower());
        Auth.Input.IsFound = false;

        await Auth.SwitchStep(ProcessStep.RegistrationDetails);
        Auth.EndLoading();
    }

    private async Task OnRegistrationDetailsNextClicked()
    {
        Auth.Errors.Clear();

        if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(LastName) || string.IsNullOrEmpty(DisplayName))
        {
            Auth.Errors.Add("Please fill in all required fields.");
            return;
        }

        Auth.StartLoading();

        // Check available registration providers
        if (HasCodeProvider && HasPasswordProvider)
        {
            // Both providers available - let user choose
            await Auth.SwitchStep(ProcessStep.RegistrationProviders);
        }
        else if (HasCodeProvider)
        {
            // Only code provider - proceed with code registration
            SelectedRegistrationMethod = "code";
            await StartRegistrationProcess();
        }
        else if (HasPasswordProvider)
        {
            // Only password provider - proceed with password registration
            SelectedRegistrationMethod = "password";
            await StartRegistrationProcess();
        }
        else
        {
            Auth.Errors.Add("No registration methods available.");
        }

        Auth.EndLoading();
    }

    private async Task OnProviderClickedAsync(ProviderDefinition provider)
    {
        if (provider.Code.Equals("password", StringComparison.OrdinalIgnoreCase))
        {
            await Auth.SwitchStep(ProcessStep.EmailPasswordLogin);
            return;
        }

        Auth.StartLoading();
        VerificationValue = "";
        SelectedProvider = provider;

        if (provider.IsCodeVerification)
        {
            await RefreshVerificationCode();
            await Auth.SwitchStep(ProcessStep.CodeVerification);
        }
        else ProviderSignInChallenge(provider.Code);
    }

    private async Task OnRegistrationProviderSelected(string method)
    {
        SelectedRegistrationMethod = method;
        Auth.StartLoading();
        await StartRegistrationProcess();
        Auth.EndLoading();
    }

    private async Task StartRegistrationProcess()
    {
        if (SelectedRegistrationMethod == "code")
        {
            // Send verification code and proceed to code verification
            await RefreshVerificationCode();
            await Auth.SwitchStep(ProcessStep.RegistrationCodeVerification);
        }
        else if (SelectedRegistrationMethod == "password")
        {
            // Send verification code and proceed to password+code verification
            await RefreshVerificationCode();
            await Auth.SwitchStep(ProcessStep.RegistrationPasswordVerification);
        }
    }

    private async Task OnRegistrationCodeVerifyClicked()
    {
        Auth.StartLoading();

        switch (GetVerificationCodeResult(VerificationValue))
        {
            case VerificationCodeResult.NotValid:
                Auth.Errors.Add("The code you entered is incorrect. Please check your email/phone again or resend another one.");
                Auth.EndLoading();
                return;

            case VerificationCodeResult.Expired:
                Auth.Errors.Add("The code validity has expired, please send another one.");
                Auth.EndLoading();
                return;

            default: break;
        }

        try
        {
            // Create user with code provider only using new model
            CodeRegistrationRequest request = CodeRegistrationRequest.Create(
                Auth.Input!.Input,
                InputValueFormat,
                FirstName,
                LastName,
                DisplayName);

            User newUser = await cloudLogin.CodeRegistration(request);

            // Sign in the new user
            CustomSignInChallenge(newUser);
        }
        catch (Exception ex)
        {
            Auth.Errors.Add(ex.Message);
            Auth.EndLoading();
        }
    }

    private async Task OnRegistrationPasswordVerifyClicked()
    {
        Auth.StartLoading();

        switch (GetVerificationCodeResult(VerificationValue))
        {
            case VerificationCodeResult.NotValid:
                Auth.Errors.Add("The code you entered is incorrect. Please check your email/phone again or resend another one.");
                Auth.EndLoading();
                return;

            case VerificationCodeResult.Expired:
                Auth.Errors.Add("The code validity has expired, please send another one.");
                Auth.EndLoading();
                return;

            default: break;
        }

        if (!cloudLogin.IsValidPassword(Password))
        {
            Auth.Errors.Add("Password must contain at least one lowercase letter, one uppercase letter, one digit, one special character, and be at least 8 characters long.");
            Auth.EndLoading();
            return;
        }

        if (!Password.Equals(ConfirmPassword))
        {
            Auth.Errors.Add("Passwords must match.");
            Auth.EndLoading();
            return;
        }

        try
        {
            // Create user with password provider (includes code verification)
            PasswordRegistrationRequest request = PasswordRegistrationRequest.Create(
                Auth.Input!.Input,
                InputValueFormat,
                Password,
                FirstName,
                LastName,
                DisplayName);

            User newUser = await cloudLogin.PasswordRegistration(request);

            // Sign in the new user
            CustomSignInChallenge(newUser);
        }
        catch (Exception ex)
        {
            Auth.Errors.Add(ex.Message);
            Auth.EndLoading();
        }
    }

    private async Task OnVerifyClicked()
    {
        Auth.StartLoading();

        switch (GetVerificationCodeResult(VerificationValue))
        {
            case VerificationCodeResult.NotValid:
                Auth.Errors.Add("The code you entered is incorrect. Please check your email/WhatsApp again or resend another one.");
                return;

            case VerificationCodeResult.Expired:
                Auth.Errors.Add("The code validity has expired, please send another one.");
                return;

            default: break;
        }

        EndLoading();
        User? checkUser;

        if (SelectedProvider != null)
            checkUser = (SelectedProvider?.Code?.ToLower()) switch
            {
                "whatsapp" => await cloudLogin.GetUserByPhoneNumber(InputValue),
                "custom" => await cloudLogin.GetUserByEmailAddress(InputValue),
                _ => await cloudLogin.GetUserByEmailAddress(InputValue),
            };
        else
            checkUser = await cloudLogin.GetUserByEmailAddress(InputValue);

        if (ActionState == "AddInput")
        {
            CustomSignInChallenge(CurrentUser);
            return;
        }

        if (checkUser != null)
            CustomSignInChallenge(checkUser);
        else await Auth.SwitchStep(ProcessStep.Registration);
    }

    private async Task OnVerifyEmailClicked()
    {
        Auth.StartLoading();

        switch (GetVerificationCodeResult(VerificationValue))
        {
            case VerificationCodeResult.NotValid:
                Auth.Errors.Add("The code you entered is incorrect. Please check your email/WhatsApp again or resend another one.");
                EndLoading();
                return;

            case VerificationCodeResult.Expired:
                Auth.Errors.Add("The code validity has expired, please send another one.");
                EndLoading();
                return;

            default: break;
        }


        if (!Password.Equals(ConfirmPassword))
        {
            Auth.Errors.Add("Passwords must match.");

            EndLoading();
            return;
        }

        if (!cloudLogin.IsValidPassword(Password))
        {
            Auth.Errors.Add("Password must contain at least one lowercase letter, one uppercase letter, and be at least 6 characters long.");
            EndLoading();

            return;
        }

        User? checkUser = await cloudLogin.GetUserByEmailAddress(InputValue);

        if (checkUser == null || checkUser.ID == Guid.Empty)
        {
            Auth.Errors.Add("Error To update Password, Please Try Again Later");
            EndLoading();
            return;
        }

        //checkUser.PasswordHash = await cloudLogin.HashPassword(Password);

        await cloudLogin.UpdateUser(checkUser!);

        EndLoading();

        await Auth.SwitchStep(ProcessStep.EmailPasswordLogin);

    }
    private Task OnRegisterClicked()
    {

        if (!cloudLogin.IsValidPassword(Password))
        {
            Auth.Errors.Add("Password must contain at least one lowercase letter, one uppercase letter, and be at least 6 characters long.");
            EndLoading();

            return Task.CompletedTask;
        }

        User userValues = new()
        {
            ID = UserId,
            FirstName = FirstName,
            LastName = LastName,
            DisplayName = DisplayName
        };

        if (ActionState == "UpdateInput")
        {
            UpdateUser(userValues);
            return Task.CompletedTask;
        }
        if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(LastName) || string.IsNullOrEmpty(DisplayName))
        {
            Auth.Errors.Add("Unable to log you in. Please check that your first name, last name and your display name are correct.");
            return Task.CompletedTask;
        }

        Auth.StartLoading();

        CustomSignInChallenge(userValues);
        return Task.CompletedTask;
    }

    protected async Task OnInputKeyPressed(KeyboardEventArgs args)
    {
        if (Auth.IsLoading)
            return;

        switch (args.Key)
        {
            case "Enter":
                if (Auth.CurrentStep == ProcessStep.InputValue)
                    if (InputValueFormat == InputFormat.EmailAddress || InputValueFormat == InputFormat.PhoneNumber)
                        await OnInputNextClicked();

                if (Auth.CurrentStep == ProcessStep.RegistrationInput)
                    if (InputValueFormat == InputFormat.EmailAddress || InputValueFormat == InputFormat.PhoneNumber)
                        await OnRegistrationInputNextClicked();

                if (Auth.CurrentStep == ProcessStep.RegistrationDetails)
                    if (!string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName) && !string.IsNullOrEmpty(DisplayName))
                        await OnRegistrationDetailsNextClicked();

                if (Auth.CurrentStep == ProcessStep.CodeVerification)
                    await OnVerifyClicked();

                if (Auth.CurrentStep == ProcessStep.RegistrationCodeVerification)
                    await OnRegistrationCodeVerifyClicked();

                if (Auth.CurrentStep == ProcessStep.RegistrationPasswordVerification)
                    await OnRegistrationPasswordVerifyClicked();

                if (Auth.CurrentStep == ProcessStep.Registration)
                    if (!string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName) && !string.IsNullOrEmpty(DisplayName))
                        await OnRegisterClicked();

                break;

            case "Escape":
                if (Auth.CurrentStep == ProcessStep.InputValue)
                    InputValue = string.Empty;

                if (Auth.CurrentStep == ProcessStep.RegistrationInput)
                    InputValue = string.Empty;

                if (Auth.CurrentStep == ProcessStep.CodeVerification)
                    VerificationValue = string.Empty;

                if (Auth.CurrentStep == ProcessStep.RegistrationCodeVerification)
                    VerificationValue = string.Empty;

                if (Auth.CurrentStep == ProcessStep.RegistrationPasswordVerification)
                {
                    VerificationValue = string.Empty;
                    Password = string.Empty;
                    ConfirmPassword = string.Empty;
                }

                if (Auth.CurrentStep == ProcessStep.Registration || Auth.CurrentStep == ProcessStep.RegistrationDetails)
                {
                    FirstName = string.Empty;
                    LastName = string.Empty;
                    DisplayName = string.Empty;
                }
                break;

            default: break;
        }
    }

    //SIGN IN FUNCTIONS-------------------------------------
    private void ProviderSignInChallenge(string provider)
    {
        RedirectParameters redirectParams = RedirectParameters.CreateCustomLogin("cloudlogin", $"login/{provider}", KeepMeSignedIn, RedirectUri, true, ActionState, PrimaryEmail, null, InputValue);

        navigationManager.NavigateTo(CloudLoginShared.RedirectString(redirectParams), true);
    }

    private async Task OnEmailPasswordLoginClicked()
    {
        try
        {
            Auth.StartLoading();

            Auth.Errors.Clear();

            PasswordLoginRequest request = PasswordLoginRequest.Create(Auth.Input!.Input, Password, KeepMeSignedIn);
            bool result = await cloudLogin.PasswordLogin(request);

            EndLoading();

            if (result)
            {
                navigationManager.NavigateTo("/", true);
                return;
            }

            Auth.Errors.Add("Incorrect Email or Password");

        }
        catch (Exception ex)
        {
            Auth.Errors.Add(ex.Message);
        }
    }

    private async Task OnEmailPasswordRegisterClicked()
    {
        try
        {
            Auth.StartLoading();

            Auth.Errors.Clear();

            if (!cloudLogin.IsValidPassword(Password))
            {
                Auth.Errors.Add("Password must contain at least one lowercase letter, one uppercase letter, one digit, one special character, and be at least 8 characters long.");
                EndLoading();

                return;
            }

            PasswordRegistrationRequest request = PasswordRegistrationRequest.Create(Email, Password, FirstName, LastName);
            User user = await cloudLogin.PasswordRegistration(request);

            PasswordLoginRequest loginRequest = PasswordLoginRequest.Create(user.PrimaryEmailAddress!.Input, Password, KeepMeSignedIn);
            bool result = await cloudLogin.PasswordLogin(loginRequest);

            EndLoading();

            if (result)
            {
                navigationManager.NavigateTo("/", true);
                return;
            }

            Auth.Errors.Add("Failed to Register. Please try again later");

        }
        catch (Exception ex)
        {
            Auth.Errors.Add(ex.Message);
            EndLoading();
        }
    }

    //ACTIONS FUNCTIONS--------------------------------------
    private void UpdateUser(User user)
    {
        Dictionary<string, object> userInfo = new()
            {
                { "UserId", user.ID },
                { "FirstName", user.FirstName ?? string.Empty },
                { "LastName", user.LastName ?? string.Empty },
                { "DisplayName", user.DisplayName ?? string.Empty }
            };

        string userInfoJSON = JsonSerializer.Serialize(userInfo, CloudLoginSerialization.Options);

        RedirectParameters redirectParams = RedirectParameters.Create("CloudLogin", "Actions") with
        {
            UserInfo = userInfoJSON,
            RedirectUri = RedirectUri
        };
        navigationManager.NavigateTo(CloudLoginShared.RedirectString(redirectParams), true);
    }

    //CUSTOM SIGN IN FUNCTIONS-------------------------------
    private void CustomSignInChallenge(User user)
    {
        Dictionary<string, object> userInfo = new()
            {
                { "UserId", user.ID },
                { "FirstName", user.FirstName ?? string.Empty },
                { "LastName", user.LastName ?? string.Empty },
                { "DisplayName", user.DisplayName ?? string.Empty },
                { "Input", InputValue },
                { "Type", InputValueFormat },
            };

        string userInfoJSON = JsonSerializer.Serialize(userInfo, CloudLoginSerialization.Options);

        RedirectParameters redirectParams = RedirectParameters.CreateCustomLogin("cloudlogin", "login", KeepMeSignedIn, RedirectUri, true, ActionState, PrimaryEmail, userInfoJSON);

        navigationManager.NavigateTo(CloudLoginShared.RedirectString(redirectParams), true);
    }

    private static string CreateRandomCode(int length)
    {
        StringBuilder builder = new();

        // Use cryptographically secure random number generator
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        byte[] randomBytes = new byte[length];
        rng.GetBytes(randomBytes);

        for (int i = 0; i < length; i++)
            builder.Append(randomBytes[i] % 10); // Convert each byte to a digit 0-9

        return builder.ToString();
    }

    public async Task SendEmailCode(string receiver, string code)
    {
        try
        {
            await cloudLogin.SendEmailCode(receiver, code);
        }
        catch (Exception)
        {
            Auth.Errors.Add("Failed to send email code.");
            EndLoading();

            return;
        }
    }

    public async Task SendWhatsAppCode(string receiver, string code)
    {
        await cloudLogin.SendWhatsAppCode(receiver, code);
    }
    private VerificationCodeResult GetVerificationCodeResult(string code)
    {
        if (VerificationCode != code.Trim())
            return VerificationCodeResult.NotValid;

        if (VerificationCodeExpiry.HasValue && DateTimeOffset.UtcNow >= VerificationCodeExpiry.Value)
            return VerificationCodeResult.Expired;

        return VerificationCodeResult.Valid;
    }
    private async Task RefreshVerificationCode()
    {
        Auth.StartLoading();

        VerificationCode = CreateRandomCode(6);
        VerificationCodeExpiry = DateTimeOffset.UtcNow.AddMinutes(5);

        Auth.Errors.Clear();

        if (Auth.CurrentStep == ProcessStep.EmailForgetPassword)
            await SendEmailCode(InputValue, VerificationCode);
        else if (Auth.CurrentStep == ProcessStep.RegistrationCodeVerification || Auth.CurrentStep == ProcessStep.RegistrationPasswordVerification)
        {
            // For registration, use the input from Auth.Input
            string targetInput = Auth.Input?.Input ?? InputValue;
            InputFormat format = cloudLogin.GetInputFormat(targetInput);

            if (format == InputFormat.PhoneNumber)
                await SendWhatsAppCode(targetInput, VerificationCode);
            else
                await SendEmailCode(targetInput, VerificationCode);
        }
        else
            switch (SelectedProvider?.Code.ToLower())
            {
                case "whatsapp":
                    await SendWhatsAppCode(InputValue, VerificationCode);
                    break;

                default:
                    await SendEmailCode(InputValue, VerificationCode);

                    break;
            }
    }
    private async Task OnNewCodeClicked()
    {
        Auth.StartLoading();

        try
        {
            await RefreshVerificationCode();
            EndLoading();
        }
        catch (Exception e)
        {
            Auth.Errors.Add(e.Message);
            EndLoading();
            return;
        }
    }


    private async Task OnEmailForgetPassword()
    {
        Auth.StartLoading();

        InputValue = Email;

        if (!await Auth.CheckEmailHasRegister(InputValue))
        {
            Auth.Errors.Add("Email is not registered yet.");
            EndLoading();

            return;
        }

        await RefreshVerificationCode();
        await Auth.SwitchStep(ProcessStep.CodeEmailVerification);
        EndLoading();

    }

    //VISUAL FUNCTIONS---------------------------------------

    private void EndLoading()
    {
        Auth.EndLoading();
    }
    protected void OnDisplayNameFocus()
    {
        if (!string.IsNullOrEmpty(DisplayName) || string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
            return;

        DisplayName = $"{FirstName} {LastName}";
    }
    public string InputLabel
    {
        get
        {
            if (InputValueFormat == InputFormat.EmailAddress)
                return "Email";

            if (InputValueFormat == InputFormat.PhoneNumber)
                return "Phone";

            List<string> label = [];

            if (AvailableFormats.Contains(InputFormat.EmailAddress))
                label.Add("Email");

            if (AvailableFormats.Contains(InputFormat.PhoneNumber))
                label.Add("Phone");

            return string.Join(" or ", label);
        }
    }

    private void OnInputChanged(string newValue) => InputValue = newValue;
    protected bool InputRequired => Providers.Any(p => p.InputRequired);

    public class SelectedInput(string input)
    {
        public readonly string Input = input;
        public bool IsFound { get; set; } = false;
        public List<ProviderDefinition> Providers { get; set; } = [];
    }
}
