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

namespace AngryMonkey.Cloud.Login
{
	public partial class CloudLogin
	{
		public Action OnInput { get; set; }

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

		protected InputFormat InputValueFormat
		{
			get
			{
				if (string.IsNullOrEmpty(InputValue))
					return InputFormat.Other;

				if (IsValidEmail)
					return InputFormat.Email;

				if (IsValidPhoneNumber)
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

		protected ProcessEvent State { get; set; } = ProcessEvent.PendingSignIn;

		protected enum ProcessEvent
		{
			PendingSignIn,
			PendingLoading,
			PendingCheckNumber,
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
			}
		}

		private async Task OnNextClicked(MouseEventArgs e)
		{
			if (InputValueFormat == InputFormat.PhoneNumber)//Signin in with Phone Number
			{
				if (InputValue.StartsWith("00"))
					InputValue = $"+{InputValue[2..]}";

				if (InputValue.StartsWith("0"))
					InputValue = InputValue[1..];

				InputValue = Regex.Replace(InputValue, $"[{PhoneNumberValidCharacters}]", "");

				State = ProcessEvent.PendingCheckNumber;
			}
			else//Signin in with Email Address
			{
				//user as put the email = > check if exists
				if (string.IsNullOrEmpty(InputValue))
					return;

				State = ProcessEvent.PendingLoading;

				InputValue = InputValue.ToLower();

				CloudUser? user = await Cosmos.GetUserByEmailAddress(InputValue);

				if (user != null)//user exists => check if user is locked =>go to authorization
				{
					Providers = user.Providers.Select(key => new Provider(key)).ToList();
					State = ProcessEvent.PendingAuthorization;
				}
				else//user doesn't exist => go to registration
				{
					Providers = cloudLogin.Options.Providers.Select(key => new Provider(key.Code)).ToList();
					State = ProcessEvent.PendingProviders;
				}
			}

			StateHasChanged();
		}

		private async Task OnBackClicked(MouseEventArgs e)
		{
			WrongCode = false;
			State = ProcessEvent.PendingSignIn;
		}
		private async Task OnNewCodeClicked(MouseEventArgs e)
		{
			State = ProcessEvent.PendingLoading;
			ExpiredCode = false;
			VerificationCode = CreateRandomCode(6);
			DebugCodeShow = VerificationCode; //DEBUG ONLY
			SendMail(InputValue, VerificationCode);
			List<PatchOperation> patchOperations = new List<PatchOperation>()
			{
				PatchOperation.Replace("/EmailAddresses/0/Code",VerificationCode),
				PatchOperation.Replace("/EmailAddresses/0/VerificationCodeTime",DateTimeOffset.UtcNow)
			};

			CloudUser? user = await Cosmos.GetUserByEmailAddress(InputValue);
			string id = $"User|{user.ID}";
			PartitionKey partitionKey = new PartitionKey(user.PartitionKey);
			await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);

			State = ProcessEvent.PendingVerification;
			StateHasChanged();
		}
		private async Task OnProviderClickedAsync(Provider provider)
		{
			if (provider.Code.ToLower() == "emailaddress")// check if email code login 
			{
				//Provider is clicked we need to check if the user exists
				CloudUser? CheckUser = await Cosmos.GetUserByEmailAddress(InputValue);

				if (CheckUser != null)//user exists => match email to prover
				{
					CheckUser?.EmailAddresses?.ForEach(async email =>
					{
						//matching email with selected provider
						if (email?.Provider?.ToLower() == "emailaddress")
						{
							//email patch provider => check code
							//Check if code empty create code / if we code exist goto verification
							if (email.Code == "")//code is empty => create => push to db => goto verification
							{
								State = ProcessEvent.PendingLoading;
								VerificationCode = CreateRandomCode(6);//create code

								SendMail(InputValue, VerificationCode);
								DebugCodeShow = VerificationCode; //DEBUG ONLY
								List<PatchOperation> patchOperations = new List<PatchOperation>()
								{
									PatchOperation.Replace("/EmailAddresses/0/Code", VerificationCode),//replace empty with code
                                    PatchOperation.Replace("/EmailAddresses/0/VerificationCodeTime",DateTimeOffset.UtcNow)
								};

								string id = $"User|{CheckUser.ID}";
								PartitionKey partitionKey = new PartitionKey(CheckUser.PartitionKey);
								await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);//push to db
								State = ProcessEvent.PendingVerification;//goto verification
								StateHasChanged();

							}
							else//code exists => goto verification
							{
								DebugCodeShow = email.Code; //DEBUG ONLY
								State = ProcessEvent.PendingVerification;//goto verification
								StateHasChanged();
							}
						}
						else
						{
							//Erorr
						}
					});
				}
				else //user doesn't exist => Create code => create instance in db => IsRegistered = false/IsVerified = false/IsLocked = false
				{
					State = ProcessEvent.PendingLoading;
					VerificationCode = CreateRandomCode(6);//create code
					DebugCodeShow = VerificationCode;
					SendMail(InputValue, VerificationCode);
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
					State = ProcessEvent.PendingVerification; //go to verification
					StateHasChanged();
				}
			}
			else if (provider.Code.ToLower() == "phone")// check if phone code login 
			{

			}
			else//Login from other providers
				NavigationManager.NavigateTo($"/cloudlogin/login/{provider.Code}?emailaddress={InputValue}&redirectUri=/&KeepMeSignedIn={KeepMeSignedIn}", true);
		}
		private async Task OnVerifyClicked(MouseEventArgs e)
		{
			//Verify button clicked => check if code is right => check if expired
			//Check if IsRegistered => Login / else : Make code empty => Goto verified
			CloudUser? CheckUser = await Cosmos.GetUserByEmailAddress(InputValue);
			CheckUser?.EmailAddresses?.ForEach(async email =>
			{
				if (email?.Provider?.ToLower() == "emailaddress")
					if (VerificationValue == email.Code)
					{//Code is right => check if expired
					 //Checking if code is expired
						DateTimeOffset CheckTime = email.VerificationCodeTime.Value;
						if (DateTimeOffset.UtcNow < CheckTime.AddSeconds(30))//30 seconds expiration
						{
							//code is not expired
							if (CheckUser?.IsRegistered == false)
							{// user is not registered => Make code empty => Goto verified

								State = ProcessEvent.PendingLoading;
								List<PatchOperation> patchOperations = new List<PatchOperation>()
								{
									PatchOperation.Replace("/EmailAddresses/0/Code", ""),//code empty
                                };
								string id = $"User|{CheckUser.ID}";
								PartitionKey partitionKey = new PartitionKey(CheckUser.PartitionKey);
								await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);

								//Code is now empty goto registration
								State = ProcessEvent.PendingRegisteration;
								StateHasChanged();
							}
							else//User is registered => make code empty =>update time => Login user
							{
								State = ProcessEvent.PendingLoading;
								List<PatchOperation> patchOperations = new List<PatchOperation>()
								{
									PatchOperation.Add("/LastSignedIn", DateTimeOffset.UtcNow), //update time
                                    PatchOperation.Replace("/EmailAddresses/0/Code", "")//code empty
                                };

								string id = $"User|{CheckUser.ID}";
								PartitionKey partitionKey = new PartitionKey(CheckUser.PartitionKey);
								await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);

								NavigationManager.NavigateTo($"/cloudlogin/Custom/CustomLogin?userID={CheckUser.ID}&KeepMeSignedIn=false", true);
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

		private async Task OnRegisterClicked(MouseEventArgs e)
		{
			//Register button is clicked => IsRegistered = true / IsVerified = true => Update user info => push to database => Login user
			if (FirstName == null || LastName == null || DisplayName == null)//check if any of the input is empty
				EmptyInput = true;
			else//all input has value
			{
				EmptyInput = false;
				State = ProcessEvent.PendingLoading;
				CloudUser? user = await Cosmos.GetUserByEmailAddress(InputValue);

				List<PatchOperation> patchOperations = new List<PatchOperation>()
				{
					PatchOperation.Add("/FirstName", FirstName), //update user
                    PatchOperation.Add("/LastName", LastName), //update user
                    PatchOperation.Add("/IsRegistered", true), //X
                    PatchOperation.Add("/EmailAddresses/0/IsVerified", true), //X
                    PatchOperation.Add("/DisplayName", $"{DisplayName}"), //update user
                    PatchOperation.Add("/LastSignedIn", DateTimeOffset.UtcNow) //update user
                };

				string id = $"User|{user?.ID}";
				PartitionKey partitionKey = new PartitionKey(user?.PartitionKey);
				await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);//push to db

				//login user
				NavigationManager.NavigateTo($"/cloudlogin/Custom/CustomLogin?userID={user?.ID}&KeepMeSignedIn=false", true);
			}
		}

		private string CreateRandomCode(int length)
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
			//public string LoginUrl => $"cloudlogin/login?redirectUri=/&identity={Code}";
			public string? Name => Code switch
			{
				"microsoft" => "Microsoft",
				"google" => "Google",
				"emailaddress" => "Email Code",
				"phonenumber" => "SMS Code",
				_ => Code
			};
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
		// ^\+?[0-9(\ \.\-\(\)\\\/)]{8,20}$
		bool IsValidPhoneNumber => Regex.IsMatch(InputValue, @$"^\+?[0-9({PhoneNumberValidCharacters})]{{8,20}}$");
		private static string PhoneNumberValidCharacters => string.Join(@"\", new[] { ' ', '.', '-', '/', '\\', '(', ')' });

		public static async void SendMail(string receiver, string Code)
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
				builder.AppendLine($"code: <b>{Code}</b> <br />");

				mailMessage.To.Add(receiver);
				mailMessage.Body = builder.ToString();

				await client.SendMailAsync(mailMessage);
			}
			catch { }
		}
	}
}
