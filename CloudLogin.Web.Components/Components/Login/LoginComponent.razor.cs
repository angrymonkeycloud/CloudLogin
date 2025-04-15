using AngryMonkey.CloudLogin.Models;
using AngryMonkey.CloudLogin.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Text;
using System.Text.Json;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AngryMonkey.CloudLogin;

public partial class LoginComponent
{
    //GENERAL VARIABLES--------------------------------------
    [Parameter] public string Logo { get; set; }
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
    public string PrimaryEmail { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string DisplayName { get; set; }
    public bool KeepMeSignedIn { get; set; }
    public bool AddInputDiplay { get; set; } = false;
    private string _inputValue;
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

    // CssClass for animation and loading states
    protected string CssClass
    {
        get
        {
            var classes = new List<string>();

            if (IsLoading)
                classes.Add("_loading");

            if (AnimateStep != AnimateBodyStep.None)
                classes.Add($"_animatestep-{AnimateStep.ToString().ToLower()}");

            if (AnimateDirection != AnimateBodyDirection.None)
                classes.Add($"_animatedirection-{AnimateDirection.ToString().ToLower()}");

            return string.Join(" ", classes);
        }
    }

    //PROVIDERS VARIABLES------------------------------------
    List<ProviderDefinition> Providers { get; set; } = [];
    public bool EmailAddressEnabled => Providers.Any(key => key.HandlesEmailAddress);
    public bool PhoneNumberEnabled => Providers.Any(key => key.HandlesPhoneNumber);
    public ProviderDefinition? SelectedProvider { get; set; }
    public Action OnInput { get; set; }

    [Inject] private AuthenticationProcessService AuthService { get; set; }

    protected AuthenticationProcess CurrentProcess => AuthService.CurrentProcess;
    protected ProcessStep CurrentStep => AuthService.CurrentStep;
    protected string Title => AuthService.Title;
    protected string Subtitle => AuthService.Subtitle;
    protected bool DisplayInputValue => AuthService.DisplayInputValue;
    protected List<string> Errors => AuthService.Errors;
    protected bool IsLoading => AuthService.IsLoading;
    protected AnimateBodyStep AnimateStep => AuthService.AnimateStep;
    protected AnimateBodyDirection AnimateDirection => AuthService.AnimateDirection;

    protected bool Next { get; set; } = false;
    protected bool Preview { get; set; } = false;

    //VERIFICATION VARIABLES---------------------------------
    public string VerificationValue { get; set; }
    public bool ExpiredCode { get; set; } = false;
    public string? VerificationCode { get; set; }
    public DateTimeOffset? VerificationCodeExpiry { get; set; }

    //START FUNCTIONS----------------------------------------
    protected override async Task OnInitializedAsync()
    {
     
        if (string.IsNullOrEmpty(ActionState))
            ActionState = "login";

        Providers = await cloudLogin.GetProviders();

        if (!string.IsNullOrEmpty(ActionState))
        {
            switch (ActionState)
            {
                case "UpdateInput":
                    AuthService.StartLoading();

                    if (CurrentUser == null)
                        return;

                    _inputValue = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true).Input;
                    FirstName = CurrentUser.FirstName;
                    LastName = CurrentUser.LastName;
                    DisplayName = CurrentUser.DisplayName;
                    UserId = CurrentUser.ID;

                    await SwitchState(ProcessStep.Registration);
                    break;

                case "AddInput":
                    PrimaryEmail = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true).Input;
                    break;

                case "ChangePrimary":
                    PrimaryEmail = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true).Input;
                    await SwitchState(ProcessStep.ChangePrimary);
                    break;

                case "AddNumber":
                    PrimaryEmail = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true).Input;
                    break;

                case "AddEmail":
                    PrimaryEmail = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true).Input;
                    break;

                default:
                    break;
            }
        }

        Providers = [.. Providers.Where(p => p.InputRequired == false)];

        await base.OnInitializedAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            if (await cloudLogin.IsAuthenticated())
            {
                navigationManager.NavigateTo("/", true);
                return;
            }

            OnInput = StateHasChanged;

            await SwitchState(ProcessStep.InputValue);
        }
    }

    //BUTTONS CLICKED FUNCTIONS------------------------------
    private async Task OnInputNextClicked()
    {
        Errors.Clear();

        List<ProviderDefinition> handlePhoneNumberProviders = [.. Providers.Where(s => s.HandlesPhoneNumber == true && s.HandleUpdateOnly == false)];

        if (ActionState == "AddInput")
            handlePhoneNumberProviders = [.. Providers.Where(s => s.HandlesPhoneNumber && s.HandleUpdateOnly == true)];

        if (InputValueFormat == InputFormat.PhoneNumber && handlePhoneNumberProviders.Count == 0 && ActionState != "AddNumber")
        {
            Errors.Add("Unable to log you in. only emails are allowed.");
            return;
        }

        if (InputValueFormat != InputFormat.PhoneNumber && InputValueFormat != InputFormat.EmailAddress)
        {
            Errors.Add("Unable to log you in. Please check that your email/phone number are correct.");
            return;
        }

        if (string.IsNullOrEmpty(InputValue))
            return;

        //IsLoading = true;
        InputValue = InputValue.ToLower();

        User? user = await cloudLogin.GetUserByInput(InputValue);

        Providers = [];

        bool addAllProviders = true;

        if (user != null)
        {
            if (user.Providers.Count != 0)
            {
                string tempInputValue = InputValue;

                if (InputValueFormat == InputFormat.PhoneNumber)
                    tempInputValue = cloudLogin.GetPhoneNumber(InputValue);

                string inputProviderCode = user.Inputs.First(p => p.Input == tempInputValue).Providers.First().Code;

                List<ProviderDefinition> userProviders = [.. user.Providers.Select(key => new ProviderDefinition(key))];

                Providers.AddRange(Providers.Where(p => p.Code == inputProviderCode));

                addAllProviders = false;
            }
            UserId = user.ID;
        }
        else if (InputValueFormat == InputFormat.PhoneNumber && !InputValue.StartsWith('+'))
        {
            Errors.Add("The (+) sign followed by your country code must precede your phone number.");
            EndLoading();

            return;
        }

        if (addAllProviders)
            Providers.AddRange(Providers
                .Where(key => (key.HandlesEmailAddress && InputValueFormat == InputFormat.EmailAddress && key.HandleUpdateOnly == false)
                            || (key.HandlesPhoneNumber && InputValueFormat == InputFormat.PhoneNumber && key.HandleUpdateOnly == false)));

        if (ActionState == "AddInput")
        {
            Providers.AddRange(Providers.Where(key => key.HandleUpdateOnly == true));
            foreach (ProviderDefinition providerInside in Providers)
            {
                List<ProviderDefinition> count = [.. Providers.Where(s => s.Code == providerInside.Code)];
                if (count.Count > 1)
                {
                    Providers.Remove(providerInside);
                }
            }
        }
        if (ActionState == "AddNumber")
        {
            Providers.AddRange(Providers.Where(key => key.HandlesPhoneNumber == true));
            foreach (ProviderDefinition providerInside in Providers)
            {
                List<ProviderDefinition> count = [.. Providers.Where(s => s.Code == providerInside.Code)];
                if (count.Count > 1)
                {
                    Providers.Remove(providerInside);
                }
            }
        }
        if (ActionState == "AddEmail")
        {
            Providers.AddRange(Providers.Where(key => key.HandlesEmailAddress == true));
            foreach (ProviderDefinition providerInside in Providers)
            {
                List<ProviderDefinition> count = [.. Providers.Where(s => s.Code == providerInside.Code)];
                if (count.Count > 1)
                {
                    Providers.Remove(providerInside);
                }
            }
        }

        if (Providers.Count == 1)
            if (Providers.First().HandlesEmailAddress)
            {
                SelectedProvider = Providers.First();
                ProviderSignInChallenge(SelectedProvider.Code);
                return;
            }

        await SwitchState(ProcessStep.Providers);
    }
    private async Task OnProviderClickedAsync(ProviderDefinition provider)
    {
        if (provider.Code.Equals("password", StringComparison.OrdinalIgnoreCase))
        {
            await SwitchState(ProcessStep.EmailPasswordLogin);
            return;
        }

        StartLoading();
        VerificationValue = "";
        SelectedProvider = provider;

        if (provider.IsCodeVerification)
        {
            await RefreshVerificationCode();
            await SwitchState(ProcessStep.CodeVerification);
        }
        else ProviderSignInChallenge(provider.Code);
    }
    private async Task OnVerifyClicked()
    {
        StartLoading();

        switch (GetVerificationCodeResult(VerificationValue))
        {
            case VerificationCodeResult.NotValid:
                Errors.Add("The code you entered is incorrect. Please check your email/WhatsApp again or resend another one.");
                return;

            case VerificationCodeResult.Expired:
                Errors.Add("The code validity has expired, please send another one.");
                return;

            default: break;
        }

        EndLoading();
        User? checkUser = null;

        if (SelectedProvider != null)
        {
            switch (SelectedProvider?.Code?.ToLower())
            {
                case "whatsapp":
                    checkUser = await cloudLogin.GetUserByPhoneNumber(InputValue);
                    break;
                case "custom":
                    checkUser = await cloudLogin.GetUserByEmailAddress(InputValue);
                    break;
                default:
                    checkUser = await cloudLogin.GetUserByEmailAddress(InputValue);
                    break;
            }
        }
        else
        {
            checkUser = await cloudLogin.GetUserByEmailAddress(InputValue);
        }

        if (ActionState == "AddInput")
        {
            CustomSignInChallenge(CurrentUser);
            return;
        }

        if (checkUser != null)
            CustomSignInChallenge(checkUser);
        else await SwitchState(ProcessStep.Registration);
    }

    private async Task OnVerifyEmailClicked()
    {
        StartLoading();

        switch (GetVerificationCodeResult(VerificationValue))
        {
            case VerificationCodeResult.NotValid:
                Errors.Add("The code you entered is incorrect. Please check your email/WhatsApp again or resend another one.");
                EndLoading();
                return;

            case VerificationCodeResult.Expired:
                Errors.Add("The code validity has expired, please send another one.");
                EndLoading();
                return;

            default: break;
        }


        if (!Password.Equals(ConfirmPassword))
        {
            Errors.Add("Passwords must match.");
            
            EndLoading();
            return;
        }

        if (!cloudLogin.IsValidPassword(Password))
        {
            Errors.Add("Password must contain at least one lowercase letter, one uppercase letter, and be at least 6 characters long.");
            EndLoading();

            return;
        }

        User? checkUser = null;

        checkUser = await cloudLogin.GetUserByEmailAddress(InputValue);

        if (checkUser.ID == Guid.Empty || checkUser == null)
        {
            Errors.Add("Error To update Password, Please Try Again Later");
            EndLoading();
            return;
        }

        checkUser.PasswordHash = await cloudLogin.HashPassword(Password);

        await cloudLogin.UpdateUser(checkUser!);

        EndLoading();

        await SwitchState(ProcessStep.EmailPasswordLogin);

    }
    private Task OnRegisterClicked()
    {

        if (!cloudLogin.IsValidPassword(Password))
        {
            Errors.Add("Password must contain at least one lowercase letter, one uppercase letter, and be at least 6 characters long.");
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
            Errors.Add("Unable to log you in. Please check that your first name, last name and your display name are correct.");
            return Task.CompletedTask;
        }

        StartLoading();

        CustomSignInChallenge(userValues);
        return Task.CompletedTask;
    }
    private async Task SetPrimary(MouseEventArgs x, string input)
    {
        //navigationManager.NavigateTo($"/CloudLogin/Actions/SetPrimary?input={HttpUtility.UrlEncode(input)}&redirectUri={HttpUtility.UrlEncode(RedirectUri)}", true);

        navigationManager.NavigateTo(CloudLoginShared.RedirectString("CloudLogin", "Actions/SetPrimary", inputValue: input, redirectUri: RedirectUri), true);
    }
    protected async Task OnInputKeyPressed(KeyboardEventArgs args)
    {
        if (IsLoading)
            return;

        switch (args.Key)
        {
            case "Enter":
                if (CurrentStep == ProcessStep.InputValue)
                    if (InputValueFormat == InputFormat.EmailAddress || InputValueFormat == InputFormat.PhoneNumber)
                        await OnInputNextClicked();

                if (CurrentStep == ProcessStep.CodeVerification)
                    await OnVerifyClicked();

                if (CurrentStep == ProcessStep.Registration)
                    if (!string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName) && !string.IsNullOrEmpty(DisplayName))
                        await OnRegisterClicked();

                break;

            case "Escape":
                if (CurrentStep == ProcessStep.InputValue)
                    InputValue = null;

                if (CurrentStep == ProcessStep.CodeVerification)
                    VerificationValue = null;

                if (CurrentStep == ProcessStep.Registration)
                {
                    FirstName = null;
                    LastName = null;
                    DisplayName = null;
                }
                break;

            default: break;
        }
    }
    private async Task OnBackClicked(MouseEventArgs e)
    {
        if (CurrentStep == ProcessStep.InputValue)
            return;

        Errors.Clear();
        StateHasChanged();

        switch (CurrentStep)
        {
            case ProcessStep.CodeEmailVerification:
                await SwitchState(ProcessStep.EmailForgetPassword);
                break;
            case ProcessStep.EmailForgetPassword:
            case ProcessStep.EmailPasswordRegister:
                await SwitchState(ProcessStep.EmailPasswordLogin);
                break;
            default:
                await SwitchState(ProcessStep.InputValue);
                break;
        }

    }

    //SIGN IN FUNCTIONS-------------------------------------
    private void ProviderSignInChallenge(string provider)
    {
        //string redirectUri = $"/cloudlogin/login/{provider}?input={InputValue}&redirectUri={RedirectUri}&keepMeSignedIn={KeepMeSignedIn}&actionState={ActionState}&primaryEmail={PrimaryEmail}";

        //navigationManager.NavigateTo(redirectUri + "&samesite=true", true);

        string navigateUrl = CloudLoginShared.RedirectString("cloudlogin", $"login/{provider}", inputValue: InputValue, redirectUri: RedirectUri, keepMeSignedIn: KeepMeSignedIn.ToString(), actionState: ActionState, primaryEmail: PrimaryEmail, sameSite: true.ToString());
        navigationManager.NavigateTo(navigateUrl, true);
    }

    private async Task SwitchState(ProcessStep step)
    {
        await AuthService.SwitchStep(step);
        StateHasChanged();
    }

    private async Task OnEmailPasswordLoginClicked()
    {
        try
        {
            StartLoading();

            Errors.Clear();

            bool result = await cloudLogin.PasswordLogin(Email, Password, KeepMeSignedIn);

            EndLoading();

            if (result)
            {
                navigationManager.NavigateTo("/", true);
                return;
            }

            Errors.Add("Incorrect Email or Passowrd");

        }
        catch (Exception ex)
        {
            Errors.Add(ex.Message);
        }
    }

    private async Task OnEmailPasswordRegisterClicked()
    {
        try
        {
            StartLoading();

            Errors.Clear();

            User user = await cloudLogin.PasswordRegistration(Email, Password, FirstName, LastName);
            bool result = await cloudLogin.PasswordLogin(user.PrimaryEmailAddress!.Input, Password, KeepMeSignedIn);

            EndLoading();

            if (result)
            {
                navigationManager.NavigateTo("/", true);
                return;
            }

            Errors.Add("Failed to Register. Please try again later");

        }
        catch (Exception ex)
        {
            Errors.Add(ex.Message);
        }
    }

    //ACTIONS FUNCTIONS--------------------------------------
    private void UpdateUser(User user)
    {
        Dictionary<string, object> userInfo = new()
            {
                { "UserId", user.ID },
                { "FirstName", user.FirstName },
                { "LastName", user.LastName },
                { "DisplayName", user.DisplayName }
            };

        string userInfoJSON = JsonSerializer.Serialize(userInfo, CloudLoginSerialization.Options);

        //navigationManager.NavigateTo($"/CloudLogin/Actions/Update?userInfo={HttpUtility.UrlEncode(userInfoJSON)}&redirectUri={HttpUtility.UrlEncode(RedirectUri)}", true);
        navigationManager.NavigateTo(CloudLoginShared.RedirectString("CloudLogin", "Actions", userInfo: userInfoJSON, redirectUri: RedirectUri), true);
    }

    //CUSTOM SIGN IN FUNCTIONS-------------------------------
    private void CustomSignInChallenge(User user)
    {
        Dictionary<string, object> userInfo = new()
            {
                { "UserId", user.ID },
                { "FirstName", user.FirstName },
                { "LastName", user.LastName },
                { "DisplayName", user.DisplayName },
                { "Input", InputValue },
                { "Type", InputValueFormat },
            };

        string userInfoJSON = JsonSerializer.Serialize(userInfo, CloudLoginSerialization.Options);

        //string redirectUri = $"/cloudlogin/login/customlogin?userInfo={HttpUtility.UrlEncode(userInfoJSON)}&keepMeSignedIn={KeepMeSignedIn}&redirectUri={HttpUtility.UrlEncode(RedirectUri)}&actionState={ActionState}&primaryEmail={PrimaryEmail}";

        //navigationManager.NavigateTo(redirectUri + "&samesite=true", true);

        navigationManager.NavigateTo(CloudLoginShared.RedirectString("cloudlogin", "login", userInfo: userInfoJSON, keepMeSignedIn: KeepMeSignedIn.ToString(), redirectUri: RedirectUri, actionState: ActionState, primaryEmail: PrimaryEmail, sameSite: true.ToString()), true);
    }

    private static string CreateRandomCode(int length)
    {
        StringBuilder builder = new();

        for (int i = 0; i < length; i++)
            builder.Append(new Random().Next(0, 9));

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
            Errors.Add("Failed to send email code.");
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
        StartLoading();

        VerificationCode = CreateRandomCode(6);
        VerificationCodeExpiry = DateTimeOffset.UtcNow.AddMinutes(5);

        Errors.Clear();

        if (CurrentStep == ProcessStep.EmailForgetPassword)
            await SendEmailCode(InputValue, VerificationCode);
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
        StartLoading();

        try
        {
            await RefreshVerificationCode();
            EndLoading();
        }
        catch (Exception e)
        {
            Errors.Add(e.Message);
            EndLoading();
            return;
        }
    }

    private async Task OnEmailForgetPassword()
    {
        StartLoading();

        InputValue = Email;

        if (!await CheckEmailHasRegister(InputValue))
        {
            Errors.Add("Email is not registered yet.");
            EndLoading();

            return;
        }

        await RefreshVerificationCode();
        await SwitchState(ProcessStep.CodeEmailVerification);
        EndLoading();

    }

    private async Task<bool> CheckEmailHasRegister(string email)
    {

        User? user = await cloudLogin.GetUserByEmailAddress(email);

        return user.ID != Guid.Empty ? true : false;

    }

    //VISUAL FUNCTIONS---------------------------------------

    private async void StartLoading()
    {
        AuthService.StartLoading();
        
    }
    private void EndLoading()
    {
        AuthService.EndLoading();
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

    protected bool AllowTextIntput => Providers.Any(p => p.Code.Equals("custom", StringComparison.OrdinalIgnoreCase));
}
