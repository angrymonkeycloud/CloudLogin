using AngryMonkey.Cloud.Geography;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Newtonsoft.Json;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
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

		public Action OnInput { get; set; }
		public Guid UserId { get; set; } = Guid.NewGuid();

		private string _inputValue;
		Provider providerType { get; set; }

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

			IsLoading = false;
			StateHasChanged();
		}

		protected enum ProcessState
		{
			PendingSignIn,
			PendingAuthorization,
			PendingProviders,
			PendingVerification,
			PendingRegisteration
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
				InputValue = cloudGeography.PhoneNumbers.Get(InputValue).GetFullPhoneNumber();

				user = await Cosmos.GetUserByPhoneNumber(InputValue);
			}
			else
				user = await Cosmos.GetUserByEmailAddress(InputValue);

			CheckUser(user);
		}

		private void CheckUser(CloudUser? user)
		{
			if (user != null) // Existing user
			{
				Providers = user.Providers.Select(key => new Provider(key)).ToList();

				foreach (Provider provider in cloudLogin.Options.Providers.Where(key => key.AlwaysShow))
					if (!Providers.Any(key => key.Code == provider.Code))
						if ((InputValueFormat == InputFormat.EmailAddress && provider.HandlesEmailAddress)
							|| (InputValueFormat == InputFormat.PhoneNumber && provider.HandlesPhoneNumber))
							Providers.Add(provider);
				UserId = user.ID;

				SwitchState(ProcessState.PendingAuthorization);
			}
			else // New user
			{
				if (InputValueFormat == InputFormat.PhoneNumber && !InputValue.StartsWith('+'))
				{
					Errors.Add("Phone number must start with the country code preceding with a + sign");
					IsLoading = false;

					return;
				}

				Providers = cloudLogin.Options.Providers
				.Where(key => (key.HandlesEmailAddress && InputValueFormat == InputFormat.EmailAddress)
							|| (key.HandlesPhoneNumber && InputValueFormat == InputFormat.PhoneNumber))
				.ToList();

				SwitchState(ProcessState.PendingProviders);
			}
		}

		private async Task OnContinueClicked(MouseEventArgs e)
		{
			IsLoading = true;
			CloudUser? user = await Cosmos.GetUserByPhoneNumber(InputValue);
			CheckUser(user);
		}

		private async Task OnBackClicked(MouseEventArgs e)
		{
			if (State == ProcessState.PendingSignIn)
				return;

			WrongCode = false;
			SwitchState(ProcessState.PendingSignIn);
		}

		private async Task RefreshVerificationCode()
		{
			VerificationCode = CreateRandomCode(6);
			VerificationCodeExpiry = DateTimeOffset.UtcNow.AddMinutes(5);

			ExpiredCode = false;
			WrongCode = false;

			if (InputValueFormat == InputFormat.EmailAddress)
				await SendEmail(InputValue, VerificationCode);

			if (InputValueFormat == InputFormat.PhoneNumber)
				await SendWhatsAppCode(InputValue, VerificationCode);
		}

		private async Task OnNewCodeClicked(MouseEventArgs e)
		{
			IsLoading = true;

			await RefreshVerificationCode();

			SwitchState(ProcessState.PendingVerification);
		}
		private async Task OnProviderClickedAsync(Provider provider)
		{
			providerType = provider;

			if (provider.Code.ToLower() == "email")
			{
				IsLoading = true;

				await RefreshVerificationCode();

				SwitchState(ProcessState.PendingVerification);
			}
			else if (provider.Code.ToLower() == "whatsapp")
			{
				IsLoading = true;

				await RefreshVerificationCode();

				SwitchState(ProcessState.PendingVerification);

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

			navigationManager.NavigateTo($"/cloudlogin/login/customlogin?userInfo={userInfoJSON}&keepMeSignedIn={KeepMeSignedIn}", true);
		}

		private async Task OnVerifyClicked(MouseEventArgs e)
		{
			IsLoading = true;

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
			if (providerType.Code.ToLower() == "email")
			{
				CloudUser? checkUser = await Cosmos.GetUserByEmailAddress(InputValue);

				if (checkUser != null)
					CustomSignInChallenge(checkUser);
				else SwitchState(ProcessState.PendingRegisteration);

			}
			else if (providerType.Code.ToLower() == "whatsapp")
			{
				CloudUser? checkUser = await Cosmos.GetUserByPhoneNumber(InputValue);

				if (checkUser != null)
					CustomSignInChallenge(checkUser);
				else SwitchState(ProcessState.PendingRegisteration);

			}
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
			IsLoading = true;

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

			for (int i = 0; i <= length; i++)
				builder.Append(new Random().Next(0, 9));

			return builder.ToString();
		}

		bool IsValidEmail => Regex.IsMatch(InputValue, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
		
		public async Task SendWhatsAppCode(string receiver, string code)
		{
			string serialize = "{\"messaging_product\": \"whatsapp\",\"recipient_type\": \"individual\",\"to\": \"" + receiver.Replace("+", "") + "\",\"type\": \"template\",\"template\": {\"name\": \"" + cloudLogin.Options.Whatsapp.Template + "\",\"language\": {\"code\": \"" + cloudLogin.Options.Whatsapp.Language + "\"},\"components\": [{\"type\": \"body\",\"parameters\": [{\"type\": \"text\",\"text\": \"" + code + "\"}]}]}}";

			using HttpRequestMessage request = new()
			{
				Method = new HttpMethod("POST"),
				RequestUri = new(cloudLogin.Options.Whatsapp.RequestUri),
				Content = new StringContent(serialize),
			};

			request.Headers.Add("Authorization", cloudLogin.Options.Whatsapp.Authorization);
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

		//private async Task SendSMSCode(string receiver, string code)
		//{
		//	TwilioClient.Init(cloudLogin.Options.Twilio.AccountId, cloudLogin.Options.Twilio.AuthenticationId);

		//	string messageBody = cloudLogin.Options.Twilio.Message.Replace("{{code}}", code);
		//	var from = new Twilio.Types.PhoneNumber(cloudLogin.Options.Twilio.PhoneNumber);
		//	var to = new Twilio.Types.PhoneNumber(receiver);


		//	MessageResource message = MessageResource.Create(
		//		body: messageBody,
		//		from: from,
		//		to: to
		//	);
		//}

		public async Task SendEmail(string receiver, string code)
		{
			cloudLogin.Options.MailMessage.To.Add(receiver);
			cloudLogin.Options.MailMessage.Body = cloudLogin.Options.EmailMessageBody.Replace("{{code}}", code);

			await cloudLogin.Options.SmtpClient.SendMailAsync(cloudLogin.Options.MailMessage);
		}
	}

}
