using AngryMonkey.Cloud.Geography;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Newtonsoft.Json;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using static Microsoft.Extensions.DependencyInjection.CloudLoginConfiguration;

namespace AngryMonkey.Cloud.Login
{
	public partial class CloudLogin
	{
		public bool checkError { get; set; } = false;
		public bool IsLoading { get; set; } = false;
		protected string Title { get; set; } = string.Empty;
		protected string Subtitle { get; set; } = string.Empty;
		protected List<string> Errors { get; set; } = new List<string>();
		public Provider? SelectedProvider { get; set; }
		public Action OnInput { get; set; }
		public Guid UserId { get; set; } = Guid.NewGuid();

		private string _inputValue;

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

				return string.Join(" ", classes);
			}
		}

		[Parameter] public string Logo { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string DisplayName { get; set; }
		public string VerificationValue { get; set; }
		public bool KeepMeSignedIn { get; set; }
		public bool WrongCode { get; set; } = false;
		public bool EmptyInput { get; set; } = false;
		public bool ExpiredCode { get; set; } = false;
		internal CosmosMethods Cosmos { get; set; }
		List<Provider> Providers { get; set; } = new();
		public bool DisplayInputValue { get; set; } = false;

		public bool EmailAddressEnabled => cloudLogin.Options.Providers.Any(key => key.HandlesEmailAddress);
		public bool PhoneNumberEnabled => cloudLogin.Options.Providers.Any(key => key.HandlesPhoneNumber);

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

		protected async Task OnInputKeyPressed(KeyboardEventArgs args)
		{
			if (IsLoading)
				return;

			switch (args.Key)
			{
				case "Enter":
					if (InputValueFormat == InputFormat.EmailAddress || InputValueFormat == InputFormat.PhoneNumber)
						await OnInputNextClicked();
					break;

				case "Escape":
					InputValue = null;
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

		protected InputFormat InputValueFormat
		{
			get
			{
				if (string.IsNullOrEmpty(InputValue))
					return InputFormat.Other;

				if (IsValidEmail)
					return InputFormat.EmailAddress;

				if (cloudGeography.PhoneNumbers.IsValidPhoneNumber(InputValue))
					return InputFormat.PhoneNumber;

				return InputFormat.Other;
			}
		}

		protected ProcessState State { get; set; }
		private void SwitchState(ProcessState state)
		{
			StateHasChanged();
			State = state;

			switch (State)
			{
				case ProcessState.PendingSignIn:
					Title = "Sign in";
					Subtitle = string.Empty;
					DisplayInputValue = false;

					break;

				case ProcessState.PendingProviders:
					Title = "Register";
					Subtitle = "Choose a provider to register.";
					DisplayInputValue = true;

					break;

				case ProcessState.PendingAuthorization:
					Title = "Continue signing in";
					Subtitle = "Choose a provider to sign in.";
					DisplayInputValue = true;
					break;

				default:
					Title = "Untitled!!!";
					Subtitle = string.Empty;
					DisplayInputValue = false;
					break;
			}

			EndLoading();
		}

		protected enum ProcessState
		{
			PendingSignIn,
			PendingAuthorization,
			PendingProviders,
			PendingVerification,
			PendingRegisteration
		}

		private void StartLoading()
		{
			IsLoading = true;
		}

		private void EndLoading()
		{
			IsLoading = false;
			StateHasChanged();
		}

		protected override async Task OnAfterRenderAsync(bool firstRender)
		{
			if (firstRender)
			{
				Cosmos = new CosmosMethods(cloudLogin.Options.Cosmos.ConnectionString, cloudLogin.Options.Cosmos.DatabaseId, cloudLogin.Options.Cosmos.ContainerId);
				OnInput = () =>
				{
					//CheckFormat();
					StateHasChanged();
				};

				SwitchState(ProcessState.PendingSignIn);
			}
		}

		private async Task OnInputNextClicked()
		{
			if (string.IsNullOrEmpty(InputValue))
				return;

			Errors.Clear();

			IsLoading = true;

			InputValue = InputValue.ToLower();
			CloudUser? user;

			if (InputValueFormat == InputFormat.PhoneNumber)
			{
				PhoneNumber phoneNumber = cloudGeography.PhoneNumbers.Get(InputValue);

				InputValue = phoneNumber.GetFullPhoneNumber();

				user = await Cosmos.GetUserByPhoneNumber(phoneNumber);
			}
			else
				user = await Cosmos.GetUserByEmailAddress(InputValue);

			Providers = new List<Provider>();

			if (cloudLogin.Options.EmailSendCodeRequest != null && InputValueFormat == InputFormat.EmailAddress)
				Providers.Add(new Provider(string.Empty) { Label = "Email Code", IsCodeVerification = true });

			// Existing user

			if (user != null)
			{
				if (user.Providers.Any())
					Providers.AddRange(user.Providers.Select(key => new Provider(key)).ToList());
				else Providers.AddRange(cloudLogin.Options.Providers
					.Where(key => (key.HandlesEmailAddress && InputValueFormat == InputFormat.EmailAddress)
								|| (key.HandlesPhoneNumber && InputValueFormat == InputFormat.PhoneNumber)));

				UserId = user.ID;

				SwitchState(ProcessState.PendingAuthorization);
			}

			// New user

			else
			{
				if (InputValueFormat == InputFormat.PhoneNumber && !InputValue.StartsWith('+'))
				{
					Errors.Add("Phone number must start with the country code preceding with a + sign");
					EndLoading();

					return;
				}

				Providers.AddRange(cloudLogin.Options.Providers
				.Where(key => (key.HandlesEmailAddress && InputValueFormat == InputFormat.EmailAddress)
							|| (key.HandlesPhoneNumber && InputValueFormat == InputFormat.PhoneNumber)));

				SwitchState(ProcessState.PendingProviders);
			}
		}

		//private async Task OnContinueClicked(MouseEventArgs e)
		//{
		//	StartLoading();
		//	CloudUser? user = await Cosmos.GetUserByPhoneNumber(InputValue);
		//	CheckUser(user);
		//}

		private async Task OnBackClicked(MouseEventArgs e)
		{
			if (State == ProcessState.PendingSignIn)
				return;

			WrongCode = false;
			SwitchState(ProcessState.PendingSignIn);
		}

		private async Task RefreshVerificationCode()
		{
			Errors.Clear();
			StartLoading();

			VerificationCode = CreateRandomCode(6);
			VerificationCodeExpiry = DateTimeOffset.UtcNow.AddMinutes(5);

			ExpiredCode = false;
			WrongCode = false;

			switch (SelectedProvider?.Code.ToLower())
			{
				case "whatsapp":
					await SendWhatsAppCode((Whataspp)SelectedProvider, InputValue, VerificationCode);
					break;

				default:
					if (cloudLogin.Options.EmailSendCodeRequest == null)
						throw new Exception("Email Code is not configured.");

					await cloudLogin.Options.EmailSendCodeRequest.Invoke(new SendCodeValue(VerificationCode, InputValue));

					break;
			}
		}

		private async Task OnNewCodeClicked()
		{
			StartLoading();

			try
			{
				await RefreshVerificationCode();
			}
			catch (Exception e)
			{
				Errors.Add(e.Message);
				EndLoading();
				return;
			}

			SwitchState(ProcessState.PendingVerification);
		}

		private async Task OnProviderClickedAsync(Provider provider)
		{
			StartLoading();

			SelectedProvider = provider;

			if (provider.IsCodeVerification)
			{
				try
				{
					await RefreshVerificationCode();
					SwitchState(ProcessState.PendingVerification);
				}
				catch (Exception e)
				{
					Errors.Add(e.Message);
					EndLoading();
				}
			}
			else ProviderSignInChallenge(provider.Code);
		}

		private string GetPhoneNumberWithoutCode(string phoneNumber)
		{
			Country? country = cloudGeography.Countries.GuessCountryByPhoneNumber(InputValue);

			if (country == null)
				return phoneNumber;

			return phoneNumber[$"+{country.CallingCode}".Length..];
		}

		private void ProviderSignInChallenge(string provider)
		{
			navigationManager.NavigateTo($"/cloudlogin/login/{provider}?input={InputValue}&redirectUri=/&keepMeSignedIn={KeepMeSignedIn}", true);
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

			navigationManager.NavigateTo($"/cloudlogin/login/customlogin?userInfo={HttpUtility.UrlEncode(userInfoJSON)}&keepMeSignedIn={KeepMeSignedIn}", true);
		}

		private async Task OnVerifyClicked(MouseEventArgs e)
		{
			StartLoading();

			switch (GetVerificationCodeResult(VerificationValue))
			{
				case VerificationCodeResult.NotValid:
					WrongCode = true;
					return;

				case VerificationCodeResult.Expired:
					ExpiredCode = true;
					return;

				default: break;
			}

			CloudUser? checkUser = null;

			if (string.IsNullOrEmpty(SelectedProvider?.Code) && InputValueFormat == InputFormat.EmailAddress)
				checkUser = await Cosmos.GetUserByEmailAddress(InputValue);
			else if (SelectedProvider?.Code.ToLower() == "whatsapp")
				checkUser = await Cosmos.GetUserByPhoneNumber(InputValue);

			if (checkUser != null)
				CustomSignInChallenge(checkUser);
			else SwitchState(ProcessState.PendingRegisteration);
		}

		protected void OnDisplayNameFocus()
		{
			if (!string.IsNullOrEmpty(DisplayName) || string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
				return;

			DisplayName = $"{FirstName} {LastName}";
		}

		private async Task OnRegisterClicked(MouseEventArgs e)
		{
			//Register button is clicked => IsRegistered = true / IsVerified = true => Update user info => push to database => Login user
			if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(FirstName))//check if any of the input is empty
			{
				EmptyInput = true;
				StateHasChanged();
				return;
			}

			EmptyInput = false;
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

		bool IsValidEmail => Regex.IsMatch(InputValue, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");

		public async Task SendWhatsAppCode(Whataspp whatsappProvider, string receiver, string code)
		{
			string serialize = "{\"messaging_product\": \"whatsapp\",\"recipient_type\": \"individual\",\"to\": \"" + receiver.Replace("+", "") + "\",\"type\": \"template\",\"template\": {\"name\": \"" + whatsappProvider.Template + "\",\"language\": {\"code\": \"" + whatsappProvider.Language + "\"},\"components\": [{\"type\": \"body\",\"parameters\": [{\"type\": \"text\",\"text\": \"" + code + "\"}]}]}}";

			using HttpRequestMessage request = new()
			{
				Method = new HttpMethod("POST"),
				RequestUri = new(whatsappProvider.RequestUri),
				Content = new StringContent(serialize),
			};

			request.Headers.Add("Authorization", whatsappProvider.Authorization);
			request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

			try
			{
				var response = await httpClient.SendAsync(request);

				if (!response.IsSuccessStatusCode)
					throw new Exception();
			}
			catch (Exception e)
			{
				throw e;
			}
		}
	}
}
