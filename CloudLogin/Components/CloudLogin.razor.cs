using AngryMonkey.Cloud.Components;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Text;
using Microsoft.Azure.Cosmos;
using System.Text.RegularExpressions;
using AngryMonkey.Cloud.Components.Icons;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.Cloud.Login
{
	public partial class CloudLogin
	{
		public bool checkError { get; set; } = false;
		public bool IsLoading { get; set; } = false;

		public int CodeExpirationDate = 30;

		protected string Title { get; set; } = string.Empty;
		protected string Subtitle { get; set; } = string.Empty;
		protected List<string> Errors { get; set; } = new List<string>();

		public Action OnInput { get; set; }

		private string _inputValue;
		Provider providerType { get; set; }

		public string Prefix { get; set; }
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
		public string VerificationCode { get; set; }
		public string DebugCodeShow { get; set; } //DEBUG ONLY
		internal CosmosMethods Cosmos { get; set; }
		List<Provider> Providers { get; set; } = new();
		public bool DisplayInputValue { get; set; } = false;

		protected InputFormat InputValueFormat
		{
			get
			{
				if (string.IsNullOrEmpty(InputValue))
					return InputFormat.Other;

				if (IsValidEmail)
					return InputFormat.Email;

				if (cloudGeography.PhoneNumbers.IsValidPhoneNumber(InputValue))
					return InputFormat.PhoneNumber;

				return InputFormat.Other;
			}
		}

		protected enum InputFormat
		{
			Email,
			PhoneNumber,
			Other
		}


		protected ProcessState State { get; set; }
		private void SwitchState(ProcessState state)
		{
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

		private async Task OnNextClicked(MouseEventArgs e)
		{
			if (string.IsNullOrEmpty(InputValue))
				return;

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
				SwitchState(ProcessState.PendingAuthorization);
			}
			else // New user
			{
				Providers = cloudLogin.Options.Providers
					.Select(key => new Provider(key.Code))
					.Where(key => (key.ProcessEmailAddresses && InputValueFormat == InputFormat.Email)
								|| (key.ProcessPhoneNumbers && InputValueFormat == InputFormat.PhoneNumber))
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

		private async Task OnNewCodeClicked(MouseEventArgs e)
		{
			IsLoading = true;
			ExpiredCode = false;
			VerificationCode = CreateRandomCode(6);
			DebugCodeShow = VerificationCode; //DEBUG ONLY
			SendEmail(InputValue, VerificationCode);
			List<PatchOperation> patchOperations = new()
			{
				PatchOperation.Replace("/EmailAddresses/0/Code",VerificationCode),
				PatchOperation.Replace("/EmailAddresses/0/VerificationCodeTime",DateTimeOffset.UtcNow)
			};

			CloudUser? user = await Cosmos.GetUserByEmailAddress(InputValue);
			await Cosmos.Container.PatchItemAsync<dynamic>(user.CosmosId, new(user.PartitionKey), patchOperations);

			SwitchState(ProcessState.PendingVerification);
			IsLoading = false;
			StateHasChanged();
		}
		private async Task OnProviderClickedAsync(Provider provider)
		{
			providerType = provider;//for checking verification if email or phone
			if (provider.Code.ToLower() == "emailaddress")// check if email code login 
			{
				//Provider is clicked we need to check if the user exists
				CloudUser? CheckUser = await Cosmos.GetUserByEmailAddress(InputValue);

				if (CheckUser != null)//user exists => match email to prover
					CheckUser?.EmailAddresses?.ForEach(async email =>
					{
						//matching email with selected provider
						if (email?.Provider?.ToLower() == "emailaddress")
						{
							//email patch provider => check code
							//Check if code empty create code / if we code exist goto verification
							if (email.Code == "")//code is empty => create => push to db => goto verification
							{
								IsLoading = true;
								VerificationCode = CreateRandomCode(6);//create code

								SendEmail(InputValue, VerificationCode);
								DebugCodeShow = VerificationCode; //DEBUG ONLY
								List<PatchOperation> patchOperations = new()
								{
									PatchOperation.Replace("/EmailAddresses/0/Code", VerificationCode),//replace empty with code
                                    PatchOperation.Replace("/EmailAddresses/0/VerificationCodeTime",DateTimeOffset.UtcNow)
								};

								await Cosmos.Container.PatchItemAsync<dynamic>(CheckUser.CosmosId, new(CheckUser.PartitionKey), patchOperations);//push to db
								SwitchState(ProcessState.PendingVerification);
								IsLoading = false;
								StateHasChanged();

							}
							else//code exists => goto verification
							{
								IsLoading = true;
								DebugCodeShow = email.Code; //DEBUG ONLY
								SwitchState(ProcessState.PendingVerification);
								IsLoading = false;
								StateHasChanged();
							}
						}
						else
						{
							//Erorr
						}
					});
				else //user doesn't exist => Create code => create instance in db => IsRegistered = false/IsVerified = false/IsLocked = false
				{
					IsLoading = true;
					VerificationCode = CreateRandomCode(6);//create code
					DebugCodeShow = VerificationCode;
					SendEmail(InputValue, VerificationCode);
					Guid CustomUserID = Guid.NewGuid();//create id
					CloudUser user = new()
					{
						ID = CustomUserID,
						IsRegistered = false, //X
						IsLocked = false, //X
						EmailAddresses = new()
							{
								new UserEmailAddress()
								{
									EmailAddress = InputValue,
									IsPrimary = true,
									ProviderId = CustomUserID.ToString(),
									Provider = "EmailAddress",
									IsVerified = false, //x
                                    Code = VerificationCode,
									VerificationCodeTime = DateTimeOffset.UtcNow
								}
							}
					};
					await Cosmos.Container.CreateItemAsync(user);//create instance in db ..
					SwitchState(ProcessState.PendingVerification);
					IsLoading = false;
					StateHasChanged();
				}
			}
			else if (provider.Code.ToLower() == "phonenumber")// check if phone code login 
			{
				//Provider is clicked we need to check if the user exists
				CloudUser? CheckUser = await Cosmos.GetUserByEmailAddress(InputValue);

				if (CheckUser != null)//user exists => match number to prover
					CheckUser?.PhoneNumbers?.ForEach(async phonenumber =>
					{
						//matching phone number with selected provider
						if (phonenumber?.Provider?.ToLower() == "phonenumber")
						{
							if (phonenumber.Code == "")
							{
								IsLoading = true;
								VerificationCode = CreateRandomCode(6);//create code

								//SEND SMS CODE

								DebugCodeShow = VerificationCode; //DEBUG ONLY
								List<PatchOperation> patchOperations = new()
								{
									PatchOperation.Replace("/PhoneNumbers/0/Code", VerificationCode),//replace empty with code
                                    PatchOperation.Replace("/PhoneNumbers/0/VerificationCodeTime",DateTimeOffset.UtcNow)
								};

								await Cosmos.Container.PatchItemAsync<dynamic>(CheckUser.CosmosId, new(CheckUser.PartitionKey), patchOperations);//push to db
								SwitchState(ProcessState.PendingVerification);
								IsLoading = false;

								StateHasChanged();
							}
							else
							{
								IsLoading = true;
								DebugCodeShow = phonenumber.Code; //DEBUG ONLY
								SwitchState(ProcessState.PendingVerification);
								StateHasChanged();
							}
						}
						else
						{
							//error
						}
					});
				else//user doesn't exist => Create code => create instance in db => IsRegistered = false/IsVerified = false/IsLocked = false
				{
					IsLoading = true;
					VerificationCode = CreateRandomCode(6);//create code
					DebugCodeShow = VerificationCode;

					//SEND PHONE NUMBER

					Country? country = cloudGeography.Countries.GuessCountryByPhoneNumber(InputValue);
					Guid CustomUserID = Guid.NewGuid();//create id
					CloudUser user = new()
					{
						ID = CustomUserID,
						IsRegistered = false, //X
						IsLocked = false, //X
						PhoneNumbers = new()
							{
								new UserPhoneNumber()
								{
									CountryCode = country.Code,
									CountryCallingCode = country.CallingCode,
									PhoneNumber = GetPhoneNumberWithoutCode(InputValue),
									Provider = "PhoneNumber",
									ProviderId = CustomUserID.ToString(),
									IsPrimary = true,
									IsVerified = false, //x
                                    Code = VerificationCode,
									VerificationCodeTime = DateTimeOffset.UtcNow
								}
							}
					};
					await Cosmos.Container.CreateItemAsync(user);//create instance in db ..
					SwitchState(ProcessState.PendingVerification);
					IsLoading = false;
					StateHasChanged();
				}
			}
			else//Login from other providers
				navigationManager.NavigateTo($"/cloudlogin/login/{provider.Code}?emailaddress={InputValue}&redirectUri=/&KeepMeSignedIn={KeepMeSignedIn}", true);
		}

		private string GetPhoneNumberWithoutCode(string phoneNumber)
		{
			Country? country = cloudGeography.Countries.GuessCountryByPhoneNumber(InputValue);

			if (country == null)
				return phoneNumber;

			return phoneNumber[$"+{country.CallingCode}".Length..];
		}

		private async Task OnVerifyClicked(MouseEventArgs e)
		{
			if (providerType.Code.ToLower() == "emailaddress")//verifying as email adress
			{//Verify button clicked => check if code is right => check if expired
			 //Check if IsRegistered => Login / else : Make code empty => Goto verified
				CloudUser? CheckUser = await Cosmos.GetUserByEmailAddress(InputValue);
				CheckUser?.EmailAddresses?.ForEach(async email =>
				{
					if (email?.Provider?.ToLower() == "emailaddress")
						if (VerificationValue == email.Code)
						{//Code is right => check if expired
						 //Checking if code is expired
							DateTimeOffset CheckTime = email.VerificationCodeTime.Value;
							if (DateTimeOffset.UtcNow < CheckTime.AddSeconds(CodeExpirationDate))//30 seconds expiration
							{
								//code is not expired
								if (CheckUser?.IsRegistered == false)
								{// user is not registered => Make code empty => Goto verified

									IsLoading = true;
									List<PatchOperation> patchOperations = new()
								{
									PatchOperation.Replace("/EmailAddresses/0/Code", ""),//code empty
                                };

									await Cosmos.Container.PatchItemAsync<dynamic>(CheckUser.CosmosId, new(CheckUser.PartitionKey), patchOperations);

									//Code is now empty goto registration
									SwitchState(ProcessState.PendingRegisteration);
									IsLoading = false;
									StateHasChanged();
								}
								else//User is registered => make code empty =>update time => Login user
								{
									IsLoading = true;
									List<PatchOperation> patchOperations = new()
								{
									PatchOperation.Add("/LastSignedIn", DateTimeOffset.UtcNow), //update time
                                    PatchOperation.Replace("/EmailAddresses/0/Code", "")//code empty
                                };

									await Cosmos.Container.PatchItemAsync<dynamic>(CheckUser.CosmosId, new(CheckUser.PartitionKey), patchOperations);

									navigationManager.NavigateTo($"/cloudlogin/Custom/CustomLogin?userID={CheckUser.ID}&KeepMeSignedIn=false", true);
								}
							}
							else//code is expired
							{
								WrongCode = false;
								ExpiredCode = true;
							}
						}
						else //Code is wrong
							WrongCode = true;
				});
			}
			else if (providerType.Code.ToLower() == "phonenumber")//verifying as phone number
			{//Verify button clicked => check if code is right => check if expired
			 //Check if IsRegistered => Login / else : Make code empty => Goto verified
				CloudUser? CheckUser = await Cosmos.GetUserByPhoneNumber(InputValue);
				CheckUser?.PhoneNumbers?.ForEach(async phonenumber =>
				{
					if (phonenumber?.Provider?.ToLower() == "phonenumber")
						if (VerificationValue == phonenumber.Code)
						{//Code is right => check if expired
						 //Checking if code is expired
							DateTimeOffset CheckTime = phonenumber.VerificationCodeTime.Value;
							if (DateTimeOffset.UtcNow < CheckTime.AddSeconds(CodeExpirationDate))//30 seconds expiration
							{
								//code is not expired
								if (CheckUser?.IsRegistered == false)
								{// user is not registered => Make code empty => Goto verified

									IsLoading = true;
									List<PatchOperation> patchOperations = new()
									{
										PatchOperation.Replace("/PhoneNumbers/0/Code", ""),//code empty
                                    };

									await Cosmos.Container.PatchItemAsync<dynamic>(CheckUser.CosmosId, new(CheckUser.PartitionKey), patchOperations);

									//Code is now empty goto registration
									SwitchState(ProcessState.PendingRegisteration);
									IsLoading = false;
									StateHasChanged();
								}
								else//User is registered => make code empty =>update time => Login user
								{
									IsLoading = true;
									List<PatchOperation> patchOperations = new()
								{
									PatchOperation.Add("/LastSignedIn", DateTimeOffset.UtcNow), //update time
                                    PatchOperation.Replace("/PhoneNumbers/0/Code", "")//code empty
                                };


									await Cosmos.Container.PatchItemAsync<dynamic>(CheckUser.CosmosId, new(CheckUser.PartitionKey), patchOperations);

									navigationManager.NavigateTo($"/cloudlogin/Custom/CustomLogin?userID={CheckUser.ID}&KeepMeSignedIn=false", true);
								}
							}
							else//code is expired
							{
								WrongCode = false;
								ExpiredCode = true;
							}
						}
						else //Code is wrong
							WrongCode = true;
				});
			}

		}

		private async Task OnRegisterClicked(MouseEventArgs e)
		{
			//Register button is clicked => IsRegistered = true / IsVerified = true => Update user info => push to database => Login user
			if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(FirstName))//check if any of the input is empty
				EmptyInput = true;

			else//all input has value
			{
				EmptyInput = false;
				IsLoading = true;
				CloudUser? user;

				List<PatchOperation> patchOperations = new();

				if (providerType.Code.ToLower() == "emailaddress")
				{
					user = await Cosmos.GetUserByEmailAddress(InputValue);

					patchOperations.Add(PatchOperation.Add("/EmailAddresses/0/IsVerified", true));
				}
				else
				{
					user = await Cosmos.GetUserByPhoneNumber(InputValue);

					patchOperations.Add(PatchOperation.Add("/PhoneNumbers/0/IsVerified", true));
				}

				patchOperations.Add(PatchOperation.Add("/FirstName", FirstName));
				patchOperations.Add(PatchOperation.Add("/LastName", LastName));
				patchOperations.Add(PatchOperation.Add("/IsRegistered", true));
				patchOperations.Add(PatchOperation.Add("/DisplayName", $"{DisplayName}"));
				patchOperations.Add(PatchOperation.Add("/LastSignedIn", DateTimeOffset.UtcNow));

				await Cosmos.Container.PatchItemAsync<dynamic>(user.CosmosId, new PartitionKey(user?.PartitionKey), patchOperations);

				navigationManager.NavigateTo($"/cloudlogin/Custom/CustomLogin?userID={user?.ID}&KeepMeSignedIn=false", true);
			}
		}

		private static string CreateRandomCode(int length)
		{
			StringBuilder builder = new();

			for (int i = 0; i <= length; i++)
				builder.Append(new Random().Next(0, 9));

			return builder.ToString();
		}

		public class Provider
		{
			public Provider(string code)
			{
				Code = code.ToLower();
			}

			public string Code { get; private set; }
			public string? Name => Code switch
			{
				"microsoft" => "Microsoft",
				"google" => "Google",
				"emailaddress" => "Email Code",
				"phonenumber" => "SMS Code",
				_ => Code
			};

			public bool ProcessEmailAddresses => !Code.Equals("phonenumber", StringComparison.OrdinalIgnoreCase);
			public bool ProcessPhoneNumbers => !Code.Equals("emailaddress", StringComparison.OrdinalIgnoreCase);
		}

		//protected void CheckFormat()
		//{
		//    var checkEmail = IsValidEmail(Value);
		//    var checkPhoneNumber = IsValidPhoneNumber(Value);
		//    var CheckCharacters = HasCharacters(Value);

		//    if (checkEmail)
		//        InputType = InputFormat.Email;
		//    else if (checkPhoneNumber)
		//        if(!CheckCharacters)
		//            InputType = InputFormat.PhoneNumber;
		//    else if(CheckCharacters)
		//        InputType = InputFormat.Other;

		//    StateHasChanged();
		//}

		bool IsValidEmail => Regex.IsMatch(InputValue, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");

		public static async void SendEmail(string receiver, string Code)
		{
			string smtpEmail = "AngryMonkeyDev@gmail.com";
			string smtpPassword = "nllvbaqoxvfqsssh";

			try
			{
				SmtpClient client = new("smtp.gmail.com", 587)
				{
					EnableSsl = true,
					DeliveryMethod = SmtpDeliveryMethod.Network,
					UseDefaultCredentials = false,
					Credentials = new System.Net.NetworkCredential(smtpEmail, smtpPassword)
				};

				MailMessage mailMessage = new()
				{
					From = new MailAddress(smtpEmail, "Cloud Login"),
					Subject = "Login Code",
					IsBodyHtml = true,
				};

				StringBuilder builder = new();
				builder.AppendLine("<div style=\"width:300px;margin:20px auto;padding: 15px;border:1px dashed  #4569D4;text-align:center\">");
				builder.AppendLine($"<h3>Dear <b>{receiver.Split("@")[0]}</b>,</h3>");
				builder.AppendLine("<p>We recevied a request to login page.</p>");
				builder.AppendLine("<p style=\"margin-top: 0;\">Enter the following password login code:</p>");
				builder.AppendLine("<div style=\"width:150px;border:1px solid #4569D4;margin: 0 auto;padding: 10px;text-align:center;\">");
				builder.AppendLine($"code: <b style=\"color:#202124;text-decoration:none\">{Code}</b> <br />");
				builder.AppendLine("</div>");
				builder.AppendLine("</div>");

				mailMessage.To.Add(receiver);
				mailMessage.Body = builder.ToString();

				await client.SendMailAsync(mailMessage);
			}
			catch { }
		}
	}
}
