using AngryMonkey.Cloud.Components;
using AngryMonkey.Cloud.Login.DataContract;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using static AngryMonkey.Cloud.Login.DataContract.User;
using Azure.Core;
using Azure;
using Microsoft.Azure.Cosmos;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using System.Security.Policy;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos.Linq;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using AngryMonkey.Cloud.Components.Icons;
using System.Runtime.CompilerServices;

namespace AngryMonkey.Cloud.Login
{
    public partial class CloudLogin
    {

        public bool checkError { get; set; } = false;
        public bool loading { get; set; } = false;
        public Action OnInput { get; set; }

        private string _value = "";

        [Parameter]
        public string Value
        {
            get => _value;
            set
            {
                if (value == _value)
                    return;

                _value = value;

                OnInput.Invoke();
            }
        }


        [Parameter] public string Imagelogo { get; set; } = "";
        [Parameter] public string FirstName { get; set; } = "";
        [Parameter] public string LastName { get; set; } = "";
        [Parameter] public string DisplayName { get; set; } = "";
        [Parameter] public string VerificationValue { get; set; } = "";
        [Parameter] public bool KeepMeSignedIn { get; set; }
        [Parameter] public bool WrongCode { get; set; } = false;
        [Parameter] public bool EmptyInput { get; set; } = false;
        [Parameter] public bool ExpiredCode { get; set; } = false;
        [Parameter] public string VerificationCode { get; set; }
        [Parameter] public string DebugCodeShow { get; set; } //DEBUG ONLY
        [Parameter] public string PhoneNumber { get; set; }
        internal CosmosMethods Cosmos { get; set; }
        List<Provider> Providers { get; set; } = new();

        protected bool EnableEmailAddressField
        {
            get
            {
                return State != ProcessEvent.PendingLoading;
            }
        }

        protected InputFormat InputType
        {
            get
            {
                if (string.IsNullOrEmpty(Value))
                    return InputFormat.Other;

                var checkEmail = IsValidEmail(Value);
                var checkPhoneNumber = IsValidPhoneNumber(Value);
                var CheckCharacters = HasCharacters(Value);

                if (checkEmail)
                    return InputFormat.Email;

                if (checkPhoneNumber && !CheckCharacters)
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
            if (InputType == InputFormat.PhoneNumber)//Signin in with Phone Number
            {
                State = ProcessEvent.PendingCheckNumber;
                PhoneNumber = Value;
                if (PhoneNumber.StartsWith("0"))
                    PhoneNumber.ElementAt(1).ToString().Replace("0", "");
                if (StartWithManyZeroes(PhoneNumber))
                    PhoneNumber = Regex.Replace(PhoneNumber, "^0*", "+");
            }
            else//Signin in with Email Address
            {
                //user as put the email = > check if exists
                if (string.IsNullOrEmpty(Value))
                    return;

                loading = true;
                // State = ProcessEvent.PendingLoading;

                Value = Value.ToLower();

                AngryMonkey.Cloud.Login.DataContract.User? user = await Cosmos.GetUserByEmailAddress(Value);

                if (user != null)//user exists => check if user is locked =>go to authorization
                {

                    if (CheckStopUser(user))//User is locked
                    {
                        //lock user etc . .
                    }
                    else
                    {

                        Providers = user.Providers.Select(key => new Provider(key)).ToList();
                        State = ProcessEvent.PendingAuthorization;
                        loading = false;
                        StateHasChanged();
                    }
                }
                else//user doesn't exist => go to registration
                {
                    Providers = cloudLogin.Options.Providers.Select(key => new Provider(key.Code)).ToList();
                    State = ProcessEvent.PendingProviders;
                    loading = false;
                    StateHasChanged();
                }
            }
        }

        private async Task OnBackClicked(MouseEventArgs e)
        {
            WrongCode = false;
            State = ProcessEvent.PendingSignIn;
        }
        private async Task OnNewCodeClicked(MouseEventArgs e)
        {
            loading = true;
            ExpiredCode = false;
            VerificationCode = CreateRandomCode(6);
            DebugCodeShow = VerificationCode; //DEBUG ONLY
            SendMail(Value, VerificationCode);
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Replace("/EmailAddresses/0/Code",VerificationCode),
                PatchOperation.Replace("/EmailAddresses/0/VerificationCodeTime",DateTimeOffset.UtcNow)
            };

            AngryMonkey.Cloud.Login.DataContract.User? user = await Cosmos.GetUserByEmailAddress(Value);
            string id = $"User|{user.ID}";
            PartitionKey partitionKey = new PartitionKey(user.PartitionKey);
            await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);

            State = ProcessEvent.PendingVerification;
            loading = false;
            StateHasChanged();
        }
        private async Task OnProviderClickedAsync(Provider provider)
        {
            if (provider.Code.ToLower() == "email")// check if email code login 
            {
                //Provider is clicked we need to check if the user exists
                AngryMonkey.Cloud.Login.DataContract.User? CheckUser = await Cosmos.GetUserByEmailAddress(Value);

                if (CheckUser != null)//user exists => match email to prover
                {
                    CheckUser?.EmailAddresses?.ForEach(async email =>
                    {
                        //matching email with selected provider
                        if (email?.Provider?.ToLower() == "email")
                        {
                            //email patch provider => check code
                            //Check if code empty create code / if we code exist goto verification
                            if (email.Code == "")//code is empty => create => push to db => goto verification
                            {
                                loading = true;
                                VerificationCode = CreateRandomCode(6);//create code

                                SendMail(Value, VerificationCode);
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
                                loading = false;
                                StateHasChanged();

                            }
                            else//code exists => goto verification
                            {
                                loading = true;
                                DebugCodeShow = email.Code; //DEBUG ONLY
                                State = ProcessEvent.PendingVerification;//goto verification
                                loading = false;
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
                    loading = true;
                    VerificationCode = CreateRandomCode(6);//create code
                    DebugCodeShow = VerificationCode;
                    SendMail(Value, VerificationCode);
                    Guid CustomUserID = Guid.NewGuid();//create id
                    AngryMonkey.Cloud.Login.DataContract.User user = new()
                    {
                        ID = CustomUserID,
                        IsRegistered = false, //X
                        IsLocked = false, //X
                        EmailAddresses = new()
                            {
                                new UserEmailAddress()
                                {
                                    EmailAddress = Value,
                                    IsPrimary = true,
                                    ProviderId = CustomUserID.ToString(),
                                    Provider = "Email",
                                    IsVerified = false, //x
                                    Code = VerificationCode,
                                    VerificationCodeTime = DateTimeOffset.UtcNow
                                }
                            }
                    };
                    await Cosmos.Container.CreateItemAsync(user);//create instance in db ..
                    State = ProcessEvent.PendingVerification; //go to verification
                    loading = false;
                    StateHasChanged();
                }
            }
            else if (provider.Code.ToLower() == "phone")// check if phone code login 
            {

            }
            else//Login from other providers
                NavigationManager.NavigateTo($"/cloudlogin/login/{provider.Code}?emailaddress={Value}&redirectUri=/&KeepMeSignedIn={KeepMeSignedIn}", true);
        }
        private async Task OnVerifyClicked(MouseEventArgs e)
        {
            //Verify button clicked => check if code is right => check if expired
            //Check if IsRegistered => Login / else : Make code empty => Goto verified
            AngryMonkey.Cloud.Login.DataContract.User? CheckUser = await Cosmos.GetUserByEmailAddress(Value);
            CheckUser?.EmailAddresses?.ForEach(async email =>
            {
                if (email?.Provider?.ToLower() == "email")
                    if (VerificationValue == email.Code)
                    {//Code is right => check if expired
                     //Checking if code is expired
                        DateTimeOffset CheckTime = email.VerificationCodeTime.Value;
                        if (DateTimeOffset.UtcNow < CheckTime.AddSeconds(30))//30 seconds expiration
                        {
                            //code is not expired
                            if (CheckUser?.IsRegistered == false)
                            {// user is not registered => Make code empty => Goto verified

                                loading = true;
                                List<PatchOperation> patchOperations = new List<PatchOperation>()
                                {
                                    PatchOperation.Replace("/EmailAddresses/0/Code", ""),//code empty
                                };
                                string id = $"User|{CheckUser.ID}";
                                PartitionKey partitionKey = new PartitionKey(CheckUser.PartitionKey);
                                await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);

                                //Code is now empty goto registration
                                State = ProcessEvent.PendingRegisteration;
                                loading = false;
                                StateHasChanged();
                            }
                            else//User is registered => make code empty =>update time => Login user
                            {
                                loading = true;
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
            if (string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(FirstName) || string.IsNullOrEmpty(FirstName))//check if any of the input is empty
                EmptyInput = true;
            else//all input has value
            {
                EmptyInput = false;
                loading = true;
                AngryMonkey.Cloud.Login.DataContract.User? user = await Cosmos.GetUserByEmailAddress(Value);

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
                "email" => "Email Code",
                _ => Code
            };
        }

        public bool CheckStopUser(AngryMonkey.Cloud.Login.DataContract.User user)
        {
            if (user.IsLocked == true)
            {
                return true;
            }
            else
            {
                return false;
            }
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

        static bool IsValidEmail(string email) => Regex.IsMatch(email, @"\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
        static bool IsValidPhoneNumber(string number) => Regex.IsMatch(number, @"[0-9]");
        static bool HasCharacters(string number) => Regex.IsMatch(number, @"[a-zA-Z]");
        static bool StartWithOneZero(string number) => Regex.IsMatch(number, @"^(?:0)\d+$");
        static bool StartWithManyZeroes(string number) => Regex.IsMatch(number, @"^(?:00)\d+$");

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
