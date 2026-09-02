using AngryMonkey.CloudLogin.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using QRCoder;
using System.Security.Cryptography;
using System.Text;

namespace AngryMonkey.CloudLogin;

/// <summary>
/// Main login component handling authentication flows
/// This component focuses purely on authentication - user management is handled by separate Account components
/// </summary>
public partial class LoginComponent : IDisposable
{

    #region Core Parameters
    [Parameter] public string? Logo { get; set; }
    [Parameter] public bool Embedded { get; set; } = false;
    [Parameter] public string? Referer { get; set; }
    [Parameter] public Guid? RequestId { get; set; }
    [Parameter] public IReadOnlyList<string> VisibleMethods { get; set; } = [];
    [Parameter] public string? Profile { get; set; }
    [Parameter] public string? Client { get; set; }

    private bool IsQrCodeRequest => RequestId.HasValue && RequestId.Value != Guid.Empty;
    
    // Legacy support for backward compatibility
    [Parameter] public string? RedirectUri { get; set; }
    [Parameter] public string? ReferredUrl { get; set; }
    
    private string RefererValue => Referer ?? ReferredUrl ?? RedirectUri ?? cloudLogin.RedirectUri ?? navigationManager.Uri;
    #endregion

    #region Authentication State
    private string Email { get; set; } = string.Empty;
    private string Password { get; set; } = string.Empty;
    private string ConfirmPassword { get; set; } = string.Empty;
    public bool KeepMeSignedIn { get; set; }
    #endregion

    #region Input Management
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

    List<CloudLoginInputFormat> AvailableFormats
    {
        get
        {
            List<CloudLoginInputFormat> formats = [];

            if (EmailAddressEnabled)
                formats.Add(CloudLoginInputFormat.EmailAddress);

            if (PhoneNumberEnabled)
                formats.Add(CloudLoginInputFormat.PhoneNumber);

            return formats;
        }
    }
    protected CloudLoginInputFormat InputValueFormat => cloudLogin.GetInputFormat(InputValue);
    #endregion

    #region UI State
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

    public Action OnInput { get; set; } = () => { };
    protected bool Next { get; set; } = false;
    protected bool Preview { get; set; } = false;
    protected bool IsQrCodeLoading { get; set; } = false;
    protected bool QrCodeError { get; set; } = false;
    protected string? QrCodeMarkup { get; set; }
    protected Guid? QrCodeRequestId { get; set; }
    private CancellationTokenSource? _qrCodePollingCancellationTokenSource;
    #endregion

    #region Provider Management
    List<CloudLoginProviderDefinitionModel> Providers { get; set; } = [];
    List<CloudLoginProviderDefinitionModel> ExternalProviders => [.. Providers.Where(key => key.IsExternal)];
    public bool EmailAddressEnabled => Providers.Any(key => key.HandlesEmailAddress);
    public bool PhoneNumberEnabled => Providers.Any(key => key.HandlesPhoneNumber);
    public CloudLoginProviderDefinitionModel? SelectedProvider { get; set; }
    #endregion

    #region Verification Management
    public string VerificationValue { get; set; } = string.Empty;
    public bool ExpiredCode { get; set; } = false;

    /// <summary>
    /// The challenge the server issued. It is a handle, not the code: this component never learns
    /// the code, never compares it, and never decides that a sign-in may proceed.
    /// </summary>
    public string? VerificationChallengeId { get; set; }

    public DateTimeOffset? VerificationCodeExpiry { get; set; }

    /// <summary>Proof of an address the server verified, spent by the registration that follows.</summary>
    public string? VerificationToken { get; set; }

    private string? _verificationAddress;
    private CloudLoginVerificationPurposes _verificationPurpose = CloudLoginVerificationPurposes.SignIn;
    #endregion

    #region Registration Data
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<CloudLoginProviderDefinitionModel> NonExternalProviders => [.. Providers.Where(key => !key.IsExternal)];
    public List<CloudLoginProviderDefinitionModel> AvailableRegistrationProviders => [.. NonExternalProviders.Where(p =>
        p.Code.Equals("code", StringComparison.OrdinalIgnoreCase) ||
        p.Code.Equals("password", StringComparison.OrdinalIgnoreCase))];
    public bool HasCodeProvider => AvailableRegistrationProviders.Any(p => p.Code.Equals("code", StringComparison.OrdinalIgnoreCase));
    public bool HasPasswordProvider => AvailableRegistrationProviders.Any(p => p.Code.Equals("password", StringComparison.OrdinalIgnoreCase));
    private bool HasMultipleRegistrationMethods => new[] { HasCodeProvider, HasPasswordProvider }.Count(x => x) > 1;
    public string? SelectedRegistrationMethod { get; set; }
    #endregion

    #region Test Mode State
    private List<CloudUserModel> TestUsers { get; set; } = [];
    private CloudLoginProviderDefinitionModel? TestModeProvider => Providers.FirstOrDefault(p => p.Code.Equals("testmode", StringComparison.OrdinalIgnoreCase));
    #endregion

    #region Lifecycle Methods
    protected override async Task OnInitializedAsync()
    {
        if (await cloudLogin.IsAuthenticated())
        {
            navigationManager.NavigateTo("/", true);
            return;
        }

        Auth.OnStateChanged += StateHasChanged;
        Providers = [.. (await cloudLogin.GetProviders()).Select(provider => provider.ToModel())];
        if (VisibleMethods.Count > 0)
            Providers = [.. Providers.Where(provider =>
                VisibleMethods.Contains(provider.Code, StringComparer.OrdinalIgnoreCase))];
        OnInput = StateHasChanged;

        await Auth.SwitchStep(ProcessStep.InputValue);
        if (VisibleMethods.Count == 1 &&
            VisibleMethods.Contains("Qr", StringComparer.OrdinalIgnoreCase))
            await ShowQrCodeLoginAsync();
        await base.OnInitializedAsync();
    }

    public void Dispose()
    {
        StopQrCodeLogin();
        Auth.OnStateChanged -= StateHasChanged;
    }
    #endregion

    #region Input Validation and Navigation
    private async Task OnInputNextClicked()
    {
        Auth.Errors.Clear();

        if (string.IsNullOrEmpty(InputValue))
            return;

        Auth.StartLoading();
        InputValue = InputValue.ToLower();

        CloudUser? user = await cloudLogin.GetUserByInput(InputValue);

        if (user != null)
        {
            // Existing user - proceed with login flow
            Auth.Input = new SelectedInput(InputValue.ToLower())
            {
                IsFound = true
            };

            foreach (string providerCode in user.Providers)
            {
                CloudLoginProviderDefinitionModel? provider = Providers.FirstOrDefault(p => p.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase));

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
                Auth.Input = new SelectedInput(InputValue.ToLower())
                {
                    IsFound = false
                };

                await Auth.SwitchStep(ProcessStep.RegistrationDetails);
            }
            else if (ExternalProviders.Count > 0)
            {
                // Every configured provider is external, so there is nothing to collect here: the
                // provider supplies the profile and the account is created when it hands the person
                // back. The Providers step already knows how to say "Sign up with" rather than
                // "Sign in with" for an input that was not found.
                //
                // Without this branch an external-only deployment dead-ends: an unknown address is
                // reported as not found with no way forward, which on a brand-new deployment means
                // nobody can ever become its first user.
                Auth.Input = new SelectedInput(InputValue.ToLower())
                {
                    IsFound = false
                };

                Auth.Input.Providers.AddRange(ExternalProviders);

                await Auth.SwitchStep(ProcessStep.Providers);
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

        if (InputValueFormat != CloudLoginInputFormat.EmailAddress && InputValueFormat != CloudLoginInputFormat.PhoneNumber)
        {
            Auth.Errors.Add("Please enter a valid email address or phone number.");
            return;
        }

        Auth.StartLoading();
        InputValue = InputValue.ToLower();

        CloudUser? user = await cloudLogin.GetUserByInput(InputValue);

        if (user != null)
        {
            Auth.Errors.Add("An account with this email/phone already exists. Please sign in instead.");
            Auth.EndLoading();
            return;
        }

        Auth.Input = new SelectedInput(InputValue.ToLower())
        {
            IsFound = false
        };

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

        if (HasMultipleRegistrationMethods)
        {
            await Auth.SwitchStep(ProcessStep.RegistrationProviders);
        }
        else if (HasCodeProvider)
        {
            SelectedRegistrationMethod = "code";
            await StartRegistrationProcess();
        }
        else if (HasPasswordProvider)
        {
            SelectedRegistrationMethod = "password";
            await StartRegistrationProcess();
        }
        else
        {
            Auth.Errors.Add("No registration methods available.");
        }

        Auth.EndLoading();
    }
    #endregion

    #region Provider Selection
    private async Task OnProviderClickedAsync(CloudLoginProviderDefinitionModel provider)
    {
        if (provider.Code.Equals("password", StringComparison.OrdinalIgnoreCase))
        {
            await Auth.SwitchStep(ProcessStep.EmailPasswordLogin);
            return;
        }

        if (provider.Code.Equals("testmode", StringComparison.OrdinalIgnoreCase))
        {
            await OnTestModeClickedAsync();
            return;
        }

        Auth.StartLoading();
        VerificationValue = "";
        SelectedProvider = provider;

        if (provider.IsCodeVerification)
        {
            await RefreshVerificationCode(InputValue, CloudLoginVerificationPurposes.SignIn);
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
    #endregion

    #region Registration Flow
    private async Task StartRegistrationProcess()
    {
        string address = Auth.Input?.Input ?? InputValue;

        if (SelectedRegistrationMethod == "code")
        {
            await RefreshVerificationCode(address, CloudLoginVerificationPurposes.Registration);
            await Auth.SwitchStep(ProcessStep.RegistrationCodeVerification);
        }
        else if (SelectedRegistrationMethod == "password")
        {
            await RefreshVerificationCode(address, CloudLoginVerificationPurposes.Registration);
            await Auth.SwitchStep(ProcessStep.RegistrationPasswordVerification);
        }
    }

    private async Task CompleteTestModeRegistration()
    {
        Auth.StartLoading();

        try
        {
            string email = GenerateTestEmail(DisplayName, TestUsers);

            CloudLoginPasswordRegistrationRequest request = CloudLoginPasswordRegistrationRequest.Create(
                email,
                CloudLoginInputFormat.EmailAddress,
                password: null,
                FirstName,
                LastName,
                DisplayName);

            CloudUser newUser = await cloudLogin.PasswordRegistration(request);
            await OnTestModeSignInAsync(newUser.ToModel());
        }
        catch (Exception ex)
        {
            Auth.Errors.Add(ex.Message);
            Auth.EndLoading();
        }
    }

    private static string GenerateTestEmail(string displayName, List<CloudUserModel> existingTestUsers)
    {
        string slug = new string([.. displayName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_')]).Trim('_').Replace("_", string.Empty);

        if (string.IsNullOrEmpty(slug))
            slug = "user";

        string baseEmail = $"{slug}@test.cloud";

        HashSet<string> existingEmails = [.. existingTestUsers
            .SelectMany(u => u.Inputs)
            .Select(i => i.Input.ToLowerInvariant())];

        if (!existingEmails.Contains(baseEmail))
            return baseEmail;

        int counter = 1;
        string candidate;
        do
        {
            candidate = $"{slug}{counter}@test.cloud";
            counter++;
        }
        while (existingEmails.Contains(candidate));

        return candidate;
    }

    private async Task OnTestModeClickedAsync()
    {
        Auth.StartLoading();
        TestUsers = [.. (await cloudLogin.GetTestUsers()).Select(user => user.ToModel())];
        FirstName = string.Empty;
        LastName = string.Empty;
        DisplayName = string.Empty;
        await Auth.SwitchStep(ProcessStep.TestMode);
        Auth.EndLoading();
    }

    private async Task OnTestModeSignInAsync(CloudUserModel user)
    {
        Auth.StartLoading();
        Auth.Errors.Clear();

        try
        {
            if (await cloudLogin.TestLogin(user.ID, KeepMeSignedIn))
            {
                await NavigateToRefererAsync();
                return;
            }

            Auth.Errors.Add("Test sign-in is unavailable or the selected account is invalid.");
        }
        catch
        {
            Auth.Errors.Add("Test sign-in failed. Please try again.");
        }

        Auth.EndLoading();
    }

    private async Task OnTestModeCreateNewClicked()
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        DisplayName = string.Empty;
        await Auth.SwitchStep(ProcessStep.TestModeCreate);
    }

    private async Task OnTestModeCreateConfirmedAsync()
    {
        Auth.Errors.Clear();

        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName) || string.IsNullOrWhiteSpace(DisplayName))
        {
            Auth.Errors.Add("Please fill in all required fields.");
            return;
        }

        await CompleteTestModeRegistration();
    }

    private async Task OnRegistrationCodeVerifyClicked()
    {
        Auth.StartLoading();
        Auth.Errors.Clear();

        if (await VerifyEnteredCodeAsync() is null)
        {
            Auth.EndLoading();
            return;
        }

        await CompleteCodeRegistrationAsync();
    }

    /// <summary>
    /// Creates the account for an address the server just verified. The token is what proves it:
    /// registration is refused without one, so this can never create an account for someone else's
    /// address.
    /// </summary>
    private async Task CompleteCodeRegistrationAsync()
    {
        try
        {
            CloudLoginCodeRegistrationRequest request = CloudLoginCodeRegistrationRequest.Create(
                Auth.Input!.Input,
                InputValueFormat,
                FirstName,
                LastName,
                DisplayName,
                VerificationToken,
                KeepMeSignedIn);

            await cloudLogin.CodeRegistration(request);

            // CodeRegistration signs the new account in as it creates it.
            VerificationToken = null;
            Auth.EndLoading();
            await NavigateToRefererAsync();
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
        Auth.Errors.Clear();

        if (await VerifyEnteredCodeAsync() is null)
        {
            Auth.EndLoading();
            return;
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
            CloudLoginPasswordRegistrationRequest request = CloudLoginPasswordRegistrationRequest.Create(
                Auth.Input!.Input,
                InputValueFormat,
                Password,
                FirstName,
                LastName,
                DisplayName);

            CloudUser newUser = await cloudLogin.PasswordRegistration(request);
            await CustomSignInChallengeAsync(newUser);
        }
        catch (Exception ex)
        {
            Auth.Errors.Add(ex.Message);
            Auth.EndLoading();
        }
    }
    #endregion

    #region Verification Flow
    private async Task OnVerifyClicked()
    {
        Auth.StartLoading();
        Auth.Errors.Clear();

        CloudLoginVerificationResult? result = await VerifyEnteredCodeAsync();

        if (result is null)
        {
            EndLoading();
            return;
        }

        // A correct code for an account that exists has already signed this browser in: the server
        // issued the cookie as it verified, so there is nothing left to ask it for.
        if (result.Status == CloudLoginVerificationStatuses.Verified)
        {
            EndLoading();
            await NavigateToRefererAsync();
            return;
        }

        // Verified, but the address has no account yet. The proof is carried into registration.
        EndLoading();
        await Auth.SwitchStep(ProcessStep.Registration);
    }

    private async Task OnVerifyEmailClicked()
    {
        Auth.StartLoading();
        Auth.Errors.Clear();

        if (await VerifyEnteredCodeAsync() is null)
        {
            EndLoading();
            return;
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

        CloudUser? checkUser = await cloudLogin.GetUserByEmailAddress(InputValue);

        if (checkUser == null || checkUser.ID == Guid.Empty)
        {
            Auth.Errors.Add("Error To update Password, Please Try Again Later");
            EndLoading();
            return;
        }

        await cloudLogin.UpdateUser(checkUser!);
        EndLoading();
        await Auth.SwitchStep(ProcessStep.EmailPasswordLogin);
    }

    private Task OnRegisterClicked()
    {
        if (!cloudLogin.IsValidPassword(Password))
        {
            Auth.Errors.Add("Password must contain at least one lowercase letter, one uppercase letter, one digit, one special character, and be at least 8 characters long.");
            EndLoading();
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(LastName) || string.IsNullOrEmpty(DisplayName))
        {
            Auth.Errors.Add("Unable to log you in. Please check that your first name, last name and your display name are correct.");
            return Task.CompletedTask;
        }

        Auth.StartLoading();

        CloudUser userValues = new()
        {
            ID = Guid.NewGuid(),
            FirstName = FirstName,
            LastName = LastName,
            DisplayName = DisplayName
        };

        return CustomSignInChallengeAsync(userValues);
    }

    #endregion

    #region Keyboard Handling
    protected async Task OnInputKeyPressed(KeyboardEventArgs args)
    {
        if (Auth.IsLoading)
            return;

        switch (args.Key)
        {
            case "Enter":
                if (Auth.CurrentStep == ProcessStep.InputValue)
                    if (InputValueFormat == CloudLoginInputFormat.EmailAddress || InputValueFormat == CloudLoginInputFormat.PhoneNumber)
                        await OnInputNextClicked();

                if (Auth.CurrentStep == ProcessStep.RegistrationInput)
                    if (InputValueFormat == CloudLoginInputFormat.EmailAddress || InputValueFormat == CloudLoginInputFormat.PhoneNumber)
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
                ClearCurrentStepInputs();
                break;

            default: break;
        }
    }

    private void ClearCurrentStepInputs()
    {
        switch (Auth.CurrentStep)
        {
            case ProcessStep.InputValue:
            case ProcessStep.RegistrationInput:
                InputValue = string.Empty;
                break;

            case ProcessStep.CodeVerification:
            case ProcessStep.RegistrationCodeVerification:
                VerificationValue = string.Empty;
                break;

            case ProcessStep.RegistrationPasswordVerification:
                VerificationValue = string.Empty;
                Password = string.Empty;
                ConfirmPassword = string.Empty;
                break;

            case ProcessStep.Registration:
            case ProcessStep.RegistrationDetails:
                FirstName = string.Empty;
                LastName = string.Empty;
                DisplayName = string.Empty;
                break;
        }
    }
    #endregion

    #region Authentication Actions
    private void ProviderSignInChallenge(string provider)
    {
        CloudLoginRedirectParameters redirectParams = CloudLoginRedirectParameters.CreateCustomLogin(
            "cloudlogin", $"login/{provider}", KeepMeSignedIn, RefererValue, true,
            string.Empty, null, InputValue, Profile, Client);

        navigationManager.NavigateTo(CloudLoginShared.RedirectString(redirectParams), true);
    }

    /// <summary>
    /// Navigates back to the page that initiated the sign-in by honoring the
    /// <c>referer</c> query parameter. Falls back to "/Account" when none is supplied.
    /// </summary>
    private async Task NavigateToRefererAsync()
    {
        string? target = Referer ?? ReferredUrl ?? RedirectUri;

        if (!string.IsNullOrWhiteSpace(target))
            try { target = System.Net.WebUtility.UrlDecode(target); } catch { }

        target = await cloudLogin.CompleteLoginRedirect(target);

        navigationManager.NavigateTo(target, forceLoad: true);
    }

    private async Task OnEmailPasswordLoginClicked()
    {
        try
        {
            Auth.StartLoading();
            Auth.Errors.Clear();

            CloudLoginPasswordLoginRequest request = CloudLoginPasswordLoginRequest.Create(Auth.Input!.Input, Password, KeepMeSignedIn);
            bool result = await cloudLogin.PasswordLogin(request);

            EndLoading();

            if (result)
            {
                await NavigateToRefererAsync();
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

            CloudLoginPasswordRegistrationRequest request = CloudLoginPasswordRegistrationRequest.Create(Email, Password, FirstName, LastName);
            CloudUser user = await cloudLogin.PasswordRegistration(request);

            CloudLoginPasswordLoginRequest loginRequest = CloudLoginPasswordLoginRequest.Create(user.PrimaryEmailAddress!.Input, Password, KeepMeSignedIn);
            bool result = await cloudLogin.PasswordLogin(loginRequest);

            EndLoading();

            if (result)
            {
                await NavigateToRefererAsync();
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

    private async Task CustomSignInChallengeAsync(CloudUser user)
    {
        if (IsQrCodeRequest)
        {
            try
            {
                await cloudLogin.CreateLoginRequest(user.ID, RequestId);
                navigationManager.NavigateTo($"/Request/{RequestId}", true);
            }
            catch
            {
                Auth.Errors.Add("Failed to complete the sign-in request for the other device.");
                Auth.EndLoading();
            }

            return;
        }

        InputValue = user.PrimaryEmailAddress?.Input ?? user.PrimaryPhoneNumber?.Input ?? user.Username ?? InputValue;
        navigationManager.NavigateTo(BuildCustomLoginUrl(user), true);
    }

    private string BuildCustomLoginUrl(CloudUser user)
    {
        string referer = RefererValue;
        bool sameSite = !Uri.TryCreate(referer, UriKind.Absolute, out _) ||
                        CloudLoginShared.IsSameOrigin(referer, cloudLogin.LoginUrl);

        return $"{cloudLogin.LoginUrl.TrimEnd('/')}/CloudLogin/Login/CustomLogin"
             + $"?userId={Uri.EscapeDataString(user.ID.ToString())}"
             + $"&keepMeSignedIn={KeepMeSignedIn.ToString().ToLowerInvariant()}"
             + $"&referer={Uri.EscapeDataString(referer)}"
             + $"&sameSite={sameSite.ToString().ToLowerInvariant()}";
    }

    private async Task ShowQrCodeLoginAsync()
    {
        StopQrCodeLogin();

        Auth.Errors.Clear();
        IsQrCodeLoading = true;
        QrCodeError = false;
        QrCodeMarkup = null;

        if (Auth.CurrentStep != ProcessStep.QrCodeLogin)
            await Auth.SwitchStep(ProcessStep.QrCodeLogin);

        Guid requestId = Guid.NewGuid();
        QrCodeRequestId = requestId;
        string requestUrl = $"{cloudLogin.LoginUrl.TrimEnd('/')}/Request/{requestId}";

        try
        {
            QrCodeMarkup = GenerateQrCodeSvg(requestUrl);
            StartQrCodePolling(requestId);
        }
        catch
        {
            QrCodeError = true;
            Auth.Errors.Add("Unable to generate the QR code right now.");
        }
        finally
        {
            IsQrCodeLoading = false;
        }
    }

    private async Task HideQrCodeLoginAsync()
    {
        StopQrCodeLogin();

        IsQrCodeLoading = false;
        QrCodeError = false;
        QrCodeMarkup = null;
        QrCodeRequestId = null;

        if (Auth.CurrentStep == ProcessStep.QrCodeLogin)
            await Auth.SwitchStep(ProcessStep.InputValue);
    }

    private static string GenerateQrCodeSvg(string url)
    {
        using QRCodeGenerator qrGenerator = new();
        using QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using SvgQRCode svgQrCode = new(qrCodeData);

        return svgQrCode.GetGraphic(4);
    }

    private void StopQrCodeLogin()
    {
        _qrCodePollingCancellationTokenSource?.Cancel();
        _qrCodePollingCancellationTokenSource?.Dispose();
        _qrCodePollingCancellationTokenSource = null;
    }

    private void StartQrCodePolling(Guid requestId)
    {
        StopQrCodeLogin();
        _qrCodePollingCancellationTokenSource = new();
        _ = PollQrCodeLoginAsync(requestId, _qrCodePollingCancellationTokenSource.Token);
    }

    private async Task PollQrCodeLoginAsync(Guid requestId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                CloudUser? user = await cloudLogin.GetUserByRequestId(requestId);

                if (user == null)
                    continue;

                await InvokeAsync(async () =>
                {
                    StopQrCodeLogin();
                    IsQrCodeLoading = false;
                    QrCodeError = false;
                    QrCodeMarkup = null;
                    QrCodeRequestId = null;
                    await QrCodeSignInAsync(user);
                    StateHasChanged();
                });

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    private Task QrCodeSignInAsync(CloudUser user)
    {
        navigationManager.NavigateTo(BuildCustomLoginUrl(user), true);
        return Task.CompletedTask;
    }
    #endregion

    #region Code Management
    /// <summary>
    /// Asks the server for a code and keeps only the handle it answers with. Which address it goes
    /// to is still decided here, because that is what the person typed; everything after that -
    /// the code, its deadline, how many wrong answers it tolerates - belongs to the server.
    /// </summary>
    /// <remarks>
    /// Address and purpose are passed in rather than read from <c>Auth.CurrentStep</c>: every caller
    /// requests the code before switching to the step that collects it, so the step still says where
    /// the flow came from, not where it is going.
    /// </remarks>
    private async Task RefreshVerificationCode(string address, CloudLoginVerificationPurposes purpose)
    {
        Auth.StartLoading();
        Auth.Errors.Clear();

        VerificationChallengeId = null;
        VerificationToken = null;
        _verificationAddress = address;
        _verificationPurpose = purpose;

        try
        {
            CloudLoginVerificationChallenge challenge =
                await cloudLogin.SendVerificationCode(CloudLoginSendCodeRequest.Create(address, purpose));

            VerificationChallengeId = challenge.ChallengeId;
            VerificationCodeExpiry = challenge.ExpiresOn;
        }
        catch (Exception exception)
        {
            Auth.Errors.Add(exception.Message);
            EndLoading();
        }
    }

    /// <summary>Sends another code for whatever the last one was for.</summary>
    private Task RefreshVerificationCode() =>
        RefreshVerificationCode(_verificationAddress ?? InputValue, _verificationPurpose);

    /// <summary>
    /// Sends what the person typed to the server and reports what it decided. A wrong or expired
    /// code produces an error here and nothing else - the component has no way to proceed past one.
    /// </summary>
    private async Task<CloudLoginVerificationResult?> VerifyEnteredCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(VerificationChallengeId))
        {
            Auth.Errors.Add("The code validity has expired, please send another one.");
            return null;
        }

        CloudLoginVerificationResult result = await cloudLogin.VerifyCode(
            CloudLoginVerifyCodeRequest.Create(VerificationChallengeId, VerificationValue, KeepMeSignedIn));

        switch (result.Status)
        {
            case CloudLoginVerificationStatuses.Invalid:
                Auth.Errors.Add("The code you entered is incorrect. Please check your email/phone again or resend another one.");
                return null;

            case CloudLoginVerificationStatuses.Expired:
            case CloudLoginVerificationStatuses.NotFound:
                VerificationChallengeId = null;
                Auth.Errors.Add("The code validity has expired, please send another one.");
                return null;

            case CloudLoginVerificationStatuses.TooManyAttempts:
                VerificationChallengeId = null;
                Auth.Errors.Add("Too many incorrect codes were entered. Please send another one.");
                return null;

            default: break;
        }

        // A code is spent by the attempt that answers it correctly, so the challenge cannot be
        // reused whatever the caller does next.
        VerificationChallengeId = null;
        VerificationToken = result.VerificationToken;

        return result;
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

        await RefreshVerificationCode(InputValue, CloudLoginVerificationPurposes.PasswordReset);
        await Auth.SwitchStep(ProcessStep.CodeEmailVerification);
        EndLoading();
    }
    #endregion

    #region UI Helpers
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
            if (InputValueFormat == CloudLoginInputFormat.EmailAddress)
                return "Email";

            if (InputValueFormat == CloudLoginInputFormat.PhoneNumber)
                return "Phone";

            List<string> label = [];

            if (AvailableFormats.Contains(CloudLoginInputFormat.EmailAddress))
                label.Add("Email");

            if (AvailableFormats.Contains(CloudLoginInputFormat.PhoneNumber))
                label.Add("Phone");

            return string.Join(" or ", label);
        }
    }

    private void OnInputChanged(string newValue) => InputValue = newValue;
    protected bool InputRequired => Providers.Any(p => p.InputRequired);
    #endregion

    #region Nested Classes
    public class SelectedInput(string input)
    {
        public readonly string Input = input;
        public bool IsFound { get; set; } = false;
        public List<CloudLoginProviderDefinitionModel> Providers { get; set; } = [];
    }
    #endregion
}
