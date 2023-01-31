using AngryMonkey.Cloud.Geography;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Newtonsoft.Json;
using System.Text;
using System.Web;

namespace AngryMonkey.Cloud.Login
{
    public partial class CloudLogin
    {
        public bool checkError { get; set; } = false;
        public bool IsLoading { get; set; } = false;
        protected string Title { get; set; } = string.Empty;
        protected string Subtitle { get; set; } = string.Empty;
        protected bool Next { get; set; } = false;
        protected bool Preview { get; set; } = false;
        protected List<string> Errors { get; set; } = new List<string>();
        public ProviderDefinition? SelectedProvider { get; set; }
        public Action OnInput { get; set; }
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string RedirectUrl => cloudLoginClient.RedirectUrl ??= navigationManager.Uri;
        private string _inputValue;

        private AnimateBodyStep AnimateStep = AnimateBodyStep.None;
        private AnimateBodyDirection AnimateDirection = AnimateBodyDirection.None;

        private enum AnimateBodyStep
        {
            None,
            Out,
            In
        }
        private enum AnimateBodyDirection
        {
            None,
            Forward,
            Backward
        }

        [Parameter]
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

        [Parameter] public string Logo { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string DisplayName { get; set; }
        public string VerificationValue { get; set; }
        public bool KeepMeSignedIn { get; set; }
        public bool ExpiredCode { get; set; } = false;
        List<ProviderDefinition> Providers { get; set; } = new();
        public bool DisplayInputValue { get; set; } = false;


        public bool EmailAddressEnabled => cloudLoginClient.Providers.Any(key => key.HandlesEmailAddress);
        public bool PhoneNumberEnabled => cloudLoginClient.Providers.Any(key => key.HandlesPhoneNumber);

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

        protected override async Task OnInitializedAsync()
        {

            HttpClient NewClient = new HttpClient();
            NewClient.BaseAddress = new Uri(navigationManager.BaseUri);

            cloudLoginClient.HttpServer = NewClient;



            await base.OnInitializedAsync();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                OnInput = () =>
                {
                    StateHasChanged();
                };

                await SwitchState(ProcessState.InputValue);
            }
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

        public string? VerificationCode { get; set; }
        public DateTimeOffset? VerificationCodeExpiry { get; set; }

        private VerificationCodeResult GetVerificationCodeResult(string code)
        {
            if (VerificationCode != code.Trim())
                return VerificationCodeResult.NotValid;

            if (VerificationCodeExpiry.HasValue && DateTimeOffset.UtcNow >= VerificationCodeExpiry.Value)
                return VerificationCodeResult.Expired;

            return VerificationCodeResult.Valid;
        }

        private enum VerificationCodeResult
        {
            Valid,
            NotValid,
            Expired
        }

        protected InputFormat InputValueFormat => cloudLoginClient.GetInputFormat(InputValue);

        protected ProcessState State { get; set; } = ProcessState.InputValue;

        private async Task SwitchState(ProcessState state)
        {
            if (state == State)
            {
                Title = "Sign in";
                Subtitle = String.Empty;
                DisplayInputValue = false;
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

                    break;

                case ProcessState.Providers:
                    Title = "Continue signing in";
                    Subtitle = "Sign In with";
                    DisplayInputValue = true;
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
                    Title = "Register";
                    Subtitle = "Add your credentials.";
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

        protected enum ProcessState
        {
            InputValue,
            Providers,
            Registration,
            CodeVerification
        }

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

        private async Task OnInputNextClicked()
        {

            Errors.Clear();

            if (InputValueFormat != InputFormat.PhoneNumber && InputValueFormat != InputFormat.EmailAddress)
            {
                Errors.Add("Unable to log you in. Please check that your email/phone number are correct.");
                return;
            }

            if (string.IsNullOrEmpty(InputValue))
                return;


            IsLoading = true;

            InputValue = InputValue.ToLower();

            try
            {
                CloudUser? user = await cloudLoginClient.GetUserByInput(InputValue);

                Providers = new List<ProviderDefinition>();

                bool addAllProviders = true;

                if (user != null)
                {
                    if (user.Providers.Any())
                    {
                        Providers.AddRange(user.Providers.Select(key => new ProviderDefinition(key)).ToList());
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
                        .Where(key => (key.HandlesEmailAddress && InputValueFormat == InputFormat.EmailAddress)
                                    || (key.HandlesPhoneNumber && InputValueFormat == InputFormat.PhoneNumber)));

                await SwitchState(ProcessState.Providers);
            }
            catch (Exception e)
            {
                Providers.AddRange(cloudLoginClient.Providers
                        .Where(key => (key.HandlesEmailAddress && InputValueFormat == InputFormat.EmailAddress)
                                    || (key.HandlesPhoneNumber && InputValueFormat == InputFormat.PhoneNumber)));

                await SwitchState(ProcessState.Providers);
            }


        }

        private async Task OnBackClicked(MouseEventArgs e)
        {
            if (State == ProcessState.InputValue)
                return;

            Errors.Clear();

            await SwitchState(ProcessState.InputValue);

        }

        private async Task RefreshVerificationCode()
        {
            StartLoading();

            VerificationCode = CreateRandomCode(6);
            VerificationCodeExpiry = DateTimeOffset.UtcNow.AddMinutes(5);

            Errors.Clear();

            switch (SelectedProvider?.Code.ToLower())
            {
                case "whatsapp":
                    HttpClient client = new HttpClient();
                    client.BaseAddress = new Uri(navigationManager.BaseUri);
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

        private string GetPhoneNumberWithoutCode(string phoneNumber)
        {
            Country? country = cloudLoginClient.CloudGeography.Countries.GuessCountryByPhoneNumber(InputValue);

            if (country == null)
                return phoneNumber;

            return phoneNumber[$"+{country.CallingCode}".Length..];
        }

        private void ProviderSignInChallenge(string provider)
        {
            if(RedirectUrl == navigationManager.Uri)
            {
                navigationManager.NavigateTo($"/cloudlogin/login/{provider}?input={InputValue}&redirectUri={RedirectUrl}&keepMeSignedIn={KeepMeSignedIn}&samesite=true", true);
            }
            else
            {
                navigationManager.NavigateTo($"/cloudlogin/login/{provider}?input={InputValue}&redirectUri={RedirectUrl}&keepMeSignedIn={KeepMeSignedIn}&samesite=false", true);
            }
        }
        private void CustomSignInChallenge(CloudUser user)
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

            string userInfoJSON = JsonConvert.SerializeObject(userInfo);

            navigationManager.NavigateTo($"/cloudlogin/login/customlogin?userInfo={HttpUtility.UrlEncode(userInfoJSON)}&keepMeSignedIn={KeepMeSignedIn}&redirectUri={RedirectUrl}", true);
        }

        private async Task OnVerifyClicked()
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

            CloudUser? checkUser = null;

            if (string.IsNullOrEmpty(SelectedProvider?.Code) && InputValueFormat == InputFormat.EmailAddress)
                checkUser = await cloudLoginClient.GetUserByEmailAddress(InputValue);
            else if (SelectedProvider?.Code.ToLower() == "whatsapp")
                checkUser = await cloudLoginClient.GetUserByPhoneNumber(InputValue);

            if (checkUser != null)
                CustomSignInChallenge(checkUser);
            else await SwitchState(ProcessState.Registration);
        }

        protected void OnDisplayNameFocus()
        {
            if (!string.IsNullOrEmpty(DisplayName) || string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
                return;

            DisplayName = $"{FirstName} {LastName}";
        }

        private async Task OnRegisterClicked()
        {
            if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(LastName) || string.IsNullOrEmpty(DisplayName))
            {
                Errors.Add("Unable to log you in. Please check that your first name, last name and your display name are correct.");
                return;
            }


            StartLoading();

            CustomSignInChallenge(new CloudUser()
            {
                ID = UserId,
                FirstName = FirstName,
                LastName = LastName,
                DisplayName = DisplayName
            });
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
    }
}
