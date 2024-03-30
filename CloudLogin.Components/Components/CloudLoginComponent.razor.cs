using AngryMonkey.Cloud.Geography;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Text;
using System.Text.Json;

namespace AngryMonkey.CloudLogin;
public partial class CloudLoginComponent
{
    //GENERAL VARIABLES--------------------------------------
    [Parameter] public string Logo { get; set; }
    [Parameter] public string? ActionState { get; set; }
    [Parameter] public User? CurrentUser { get; set; }

    public Methods Methods = new Methods();
    public Guid UserId { get; set; } = Guid.NewGuid();
    [Parameter] public string? RedirectUri { get; set; }
    private string RedirectUriValue => RedirectUri ?? cloudLoginClient.RedirectUri ?? navigationManager.Uri;

    //INPUT VARIABLES----------------------------------------
    public string PrimaryEmail { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string DisplayName { get; set; }
    public bool KeepMeSignedIn { get; set; }
    public bool DisplayInputValue { get; set; } = false;
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
            List<InputFormat> formats = new();

            if (EmailAddressEnabled)
                formats.Add(InputFormat.EmailAddress);

            if (PhoneNumberEnabled)
                formats.Add(InputFormat.PhoneNumber);

            return formats;
        }
    }
    protected InputFormat InputValueFormat => cloudLoginClient.GetInputFormat(InputValue);


    //PROVIDERS VARIABLES------------------------------------
    List<ProviderDefinition> Providers { get; set; } = new();
    public bool EmailAddressEnabled => cloudLoginClient.Providers.Any(key => key.HandlesEmailAddress);
    public bool PhoneNumberEnabled => cloudLoginClient.Providers.Any(key => key.HandlesPhoneNumber);
    public ProviderDefinition? SelectedProvider { get; set; }
    public Action OnInput { get; set; }


    //VISUAL VARIABLES---------------------------------------
    protected ProcessState State { get; set; } = ProcessState.InputValue;
    protected string ButtonName { get; set; } = string.Empty;
    protected string Title { get; set; } = string.Empty;
    protected string Subtitle { get; set; } = string.Empty;
    protected string CssClass
    {
        get
        {
            List<string> classes = new();

            if (IsLoading)
                classes.Add("_loading");

            if (Next)
                classes.Add("_next");

            if (Preview)
                classes.Add("_preview");

            if (AnimateStep != AnimateBodyStep.None)
                classes.Add($"_animatestep-{AnimateStep.ToString().ToLower()}");

            if (AnimateDirection != AnimateBodyDirection.None)
                classes.Add($"_animatedirection-{AnimateDirection.ToString().ToLower()}");

            return string.Join(" ", classes);
        }
    }
    public bool IsLoading { get; set; } = false;
    protected bool Next { get; set; } = false;
    protected bool Preview { get; set; } = false;
    protected List<string> Errors { get; set; } = new List<string>();

    private AnimateBodyStep AnimateStep = AnimateBodyStep.None;

    private AnimateBodyDirection AnimateDirection = AnimateBodyDirection.None;

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

        //cloudLoginClient = new(navigationManager.BaseUri);

        if (!string.IsNullOrEmpty(ActionState))
        {
            switch (ActionState)
            {
                case "UpdateInput":

                    StartLoading();

                    if (CurrentUser == null)
                        return;

                    _inputValue = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true).Input;
                    FirstName = CurrentUser.FirstName;
                    LastName = CurrentUser.LastName;
                    DisplayName = CurrentUser.DisplayName;
                    UserId = CurrentUser.ID;

                    await SwitchState(ProcessState.Registration);
                    break;

                case "AddInput":
                    PrimaryEmail = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true).Input;
                    break;

                case "ChangePrimary":

                    PrimaryEmail = CurrentUser.Inputs.FirstOrDefault(i => i.IsPrimary == true).Input;

                    await SwitchState(ProcessState.ChangePrimary);
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

        Providers = cloudLoginClient.Providers.Where(p=>p.InputRequired == false).ToList();

        await base.OnInitializedAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            OnInput = StateHasChanged;

            await SwitchState(ProcessState.InputValue);
        }
    }

    //BUTTONS CLICKED FUNCTIONS------------------------------
    private async Task OnInputNextClicked()
    {
        Errors.Clear();

        List<ProviderDefinition> handlePhoneNumberProviders = cloudLoginClient.Providers.Where(s => s.HandlesPhoneNumber == true && s.HandleUpdateOnly == false).ToList();

        if (ActionState == "AddInput")
            handlePhoneNumberProviders = cloudLoginClient.Providers.Where(s => s.HandlesPhoneNumber && s.HandleUpdateOnly == true).ToList();



        if (InputValueFormat == InputFormat.PhoneNumber && handlePhoneNumberProviders.Count() == 0 && ActionState != "AddNumber")
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


        IsLoading = true;

        InputValue = InputValue.ToLower();

        User? user = await cloudLoginClient.GetUserByInput(InputValue);


        Providers = new List<ProviderDefinition>();

        bool addAllProviders = true;

        if (user != null)
        {
            if (user.Providers.Any())
            {
                string tempInputValue = InputValue;
                if (InputValueFormat == InputFormat.PhoneNumber)
                {
                    PhoneNumber phoneNumber = cloudLoginClient.CloudGeography.PhoneNumbers.Get(InputValue);
                    tempInputValue = phoneNumber.Number;
                }
                string inputProviderCode = user.Inputs.First(p => p.Input == tempInputValue).Providers.First().Code;

                List<ProviderDefinition> userProviders = user.Providers.Select(key => new ProviderDefinition(key)).ToList();

                Providers.AddRange(cloudLoginClient.Providers.Where(p => p.Code == inputProviderCode));

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
            Providers.AddRange(cloudLoginClient.Providers
                .Where(key => (key.HandlesEmailAddress && InputValueFormat == InputFormat.EmailAddress && key.HandleUpdateOnly == false)
                            || (key.HandlesPhoneNumber && InputValueFormat == InputFormat.PhoneNumber && key.HandleUpdateOnly == false)));


        if (ActionState == "AddInput")
        {
            Providers.AddRange(cloudLoginClient.Providers.Where(key => key.HandleUpdateOnly == true));
            foreach (ProviderDefinition providerInside in cloudLoginClient.Providers)
            {
                List<ProviderDefinition> count = Providers.Where(s => s.Code == providerInside.Code).ToList();
                if (count.Count() > 1)
                {
                    Providers.Remove(providerInside);
                }

            }
        }
        if (ActionState == "AddNumber")
        {
            Providers.AddRange(cloudLoginClient.Providers.Where(key => key.HandlesPhoneNumber == true));
            foreach (ProviderDefinition providerInside in cloudLoginClient.Providers)
            {
                List<ProviderDefinition> count = Providers.Where(s => s.Code == providerInside.Code).ToList();
                if (count.Count() > 1)
                {
                    Providers.Remove(providerInside);
                }
            }
        }
        if (ActionState == "AddEmail")
        {
            Providers.AddRange(cloudLoginClient.Providers.Where(key => key.HandlesEmailAddress == true));
            foreach (ProviderDefinition providerInside in cloudLoginClient.Providers)
            {
                List<ProviderDefinition> count = Providers.Where(s => s.Code == providerInside.Code).ToList();
                if (count.Count() > 1)
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

        await SwitchState(ProcessState.Providers);

    }
    private async Task OnProviderClickedAsync(ProviderDefinition provider)
    {
        StartLoading();
        VerificationValue = "";
        SelectedProvider = provider;

        if (provider.IsCodeVerification)
        {
            await RefreshVerificationCode();
            await SwitchState(ProcessState.CodeVerification);

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


        switch (SelectedProvider?.Code?.ToLower())
        {
            case "whatsapp":
                checkUser = await cloudLoginClient.GetUserByPhoneNumber(InputValue);
                break;
            case "custom":
                checkUser = await cloudLoginClient.GetUserByEmailAddress(InputValue);
                break;
            default:
                checkUser = await cloudLoginClient.GetUserByEmailAddress(InputValue);
                break;
        }

        if (ActionState == "AddInput")
        {
            CustomSignInChallenge(CurrentUser);
            return;
        }

        if (checkUser != null)
            CustomSignInChallenge(checkUser);
        else await SwitchState(ProcessState.Registration);
    }
    private Task OnRegisterClicked()
    {
        User userValues = new User()
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

        navigationManager.NavigateTo(Methods.RedirectString("CloudLogin", "Actions/SetPrimary", inputValue: input, redirectUri: RedirectUri), true);
    }
    protected async Task OnInputKeyPressed(KeyboardEventArgs args)
    {
        if (IsLoading)
            return;

        switch (args.Key)
        {
            case "Enter":
                if (State == ProcessState.InputValue)
                    if (InputValueFormat == InputFormat.EmailAddress || InputValueFormat == InputFormat.PhoneNumber)
                        await OnInputNextClicked();

                if (State == ProcessState.CodeVerification)
                    await OnVerifyClicked();

                if (State == ProcessState.Registration)
                    if (!string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName) && !string.IsNullOrEmpty(DisplayName))
                        await OnRegisterClicked();

                break;

            case "Escape":
                if (State == ProcessState.InputValue)
                    InputValue = null;

                if (State == ProcessState.CodeVerification)
                    VerificationValue = null;

                if (State == ProcessState.Registration)
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
        if (State == ProcessState.InputValue)
            return;

        Errors.Clear();

        await SwitchState(ProcessState.InputValue);

    }


    //SIGN IN FUNCTIONS-------------------------------------
    private void ProviderSignInChallenge(string provider)
    {
        //string redirectUri = $"/cloudlogin/login/{provider}?input={InputValue}&redirectUri={RedirectUri}&keepMeSignedIn={KeepMeSignedIn}&actionState={ActionState}&primaryEmail={PrimaryEmail}";

        //navigationManager.NavigateTo(redirectUri + "&samesite=true", true);

        string navigateUrl = Methods.RedirectString("cloudlogin", $"login/{provider}", inputValue: InputValue, redirectUri: RedirectUri, keepMeSignedIn: KeepMeSignedIn.ToString(), actionState: ActionState, primaryEmail: PrimaryEmail, sameSite: true.ToString());
        navigationManager.NavigateTo(navigateUrl, true); 
    }

    private async Task SwitchState(ProcessState state)
    {
        if (state == State)
        {
            Title = "Sign in";
            Subtitle = string.Empty;
            DisplayInputValue = false;

            if (ActionState == "AddInput")
            {
                AddInputDiplay = true;
                Title = "Add Input";
                Subtitle = "Add another input for your account";
            }

            StateHasChanged();
            return;
        }

        bool toNext = true;

        switch (state)
        {
            case ProcessState.InputValue:
                toNext = false;
                break;

            case ProcessState.CodeVerification:
                if (State == ProcessState.Registration)
                    toNext = false;

                break;

            case ProcessState.Providers:
                if (State != ProcessState.InputValue)
                    toNext = false;

                break;
            default: break;
        }

        AnimateDirection = toNext ? AnimateBodyDirection.Forward : AnimateBodyDirection.Backward;

        AnimateStep = AnimateBodyStep.Out;
        StateHasChanged();

        await Task.Delay(400);

        AnimateStep = AnimateBodyStep.In;
        StateHasChanged();

        State = state;

        switch (State)
        {
            case ProcessState.InputValue:
                Title = "Sign in";
                Subtitle = String.Empty;
                DisplayInputValue = false;

                if (ActionState == "AddInput")
                {
                    AddInputDiplay = true;
                    Title = "Add Input";
                    Subtitle = "Add another input for your account";
                }

                break;

            case ProcessState.Providers:
                Title = "Continue signing in";
                Subtitle = "Sign In with";
                DisplayInputValue = true;

                if (ActionState == "AddInput")
                    Title = "Continue adding input";

                break;

            case ProcessState.CodeVerification:
                string InputType = "Email";

                if (InputValueFormat == InputFormat.PhoneNumber)
                    InputType = "Whatsapp";

                Title = $"Verify your {InputType}";
                Subtitle = $"A verification code has been sent to your {InputType}, if not received, you can send another one.";
                DisplayInputValue = true;
                break;

            case ProcessState.Registration:
                if (ActionState == "UpdateInput")
                {
                    Title = "Update";
                    Subtitle = "Change your credentials.";
                    DisplayInputValue = true;
                    ButtonName = "Update";
                }
                else
                {
                    Title = "Register";
                    Subtitle = "Add your credentials.";
                    DisplayInputValue = true;
                    ButtonName = "Register";
                }
                break;


            case ProcessState.ChangePrimary:

                Title = "Set Primary";
                Subtitle = "Choose which email to put as primary.";
                DisplayInputValue = true;

                break;
            default:
                Title = "Untitled!!!";
                Subtitle = string.Empty;
                DisplayInputValue = false;
                break;
        }

        EndLoading();

        await Task.Delay(300);

        AnimateStep = AnimateBodyStep.None;
        AnimateDirection = AnimateBodyDirection.None;
        StateHasChanged();
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

        string userInfoJSON = JsonSerializer.Serialize(userInfo);

        //navigationManager.NavigateTo($"/CloudLogin/Actions/Update?userInfo={HttpUtility.UrlEncode(userInfoJSON)}&redirectUri={HttpUtility.UrlEncode(RedirectUri)}", true);
        navigationManager.NavigateTo(Methods.RedirectString("CloudLogin", "Actions", userInfo: userInfoJSON, redirectUri: RedirectUri), true);
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

        string userInfoJSON = JsonSerializer.Serialize(userInfo);

        //string redirectUri = $"/cloudlogin/login/customlogin?userInfo={HttpUtility.UrlEncode(userInfoJSON)}&keepMeSignedIn={KeepMeSignedIn}&redirectUri={HttpUtility.UrlEncode(RedirectUri)}&actionState={ActionState}&primaryEmail={PrimaryEmail}";

        //navigationManager.NavigateTo(redirectUri + "&samesite=true", true);


        navigationManager.NavigateTo(Methods.RedirectString("cloudlogin", "login", userInfo: userInfoJSON, keepMeSignedIn: KeepMeSignedIn.ToString(), redirectUri: RedirectUri, actionState: ActionState, primaryEmail: PrimaryEmail, sameSite: true.ToString()), true);
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
        await cloudLoginClient.SendEmailCode(receiver, code);
    }

    public async Task SendWhatsAppCode(string receiver, string code)
    {
        await cloudLoginClient.SendWhatsAppCode(receiver, code);
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

        HttpClient client = new HttpClient();
        client.BaseAddress = new Uri(navigationManager.BaseUri);

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


    //VISUAL FUNCTIONS---------------------------------------

    private async void StartLoading()
    {
        IsLoading = true;
        Errors.Clear();
        await Task.Delay(3000);
    }
    private void EndLoading()
    {
        IsLoading = false;
        StateHasChanged();
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

            List<string> label = new();

            if (AvailableFormats.Contains(InputFormat.EmailAddress))
                label.Add("Email");

            if (AvailableFormats.Contains(InputFormat.PhoneNumber))
                label.Add("Phone");

            return string.Join(" or ", label);
        }
    }
}
