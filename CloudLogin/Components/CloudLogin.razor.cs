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

namespace AngryMonkey.Cloud.Login
{
    public partial class CloudLogin
    {
        [Parameter] public string Value { get; set; }
        [Parameter] public string Valuetest { get; set; } = "a545590b-41d0-4ed0-afb4-35a7fd54bdf9";
        [Parameter] public string FirstName { get; set; }
        [Parameter] public string LastName { get; set; }
        [Parameter] public string DisplayName { get; set; }
        [Parameter] public string VerificationValue { get; set; }
        [Parameter] public bool KeepMeSignedIn { get; set; }
        [Parameter] public bool WrongCode { get; set; } = false;
        [Parameter] public bool EmptyInput { get; set; } = false;
        [Parameter] public bool ExpiredCode { get; set; } = false;
        [Parameter] public string VerificationCode { get; set; }
        [Parameter] public string DebugCodeShow { get; set; } //DEBUG ONLY
        internal CosmosMethods Cosmos { get; set; }
        List<Provider> Providers { get; set; } = new();

        protected bool EnableEmailAddressField
        {
            get
            {
                return State != ProcessEvent.PengindLoading;
            }
        }

        protected ProcessEvent State { get; set; } = ProcessEvent.PendingEmail;

        protected enum ProcessEvent
        {
            PendingEmail,
            PengindLoading,
            PendingAuthorization,
            PendingProviders,
            PendingVerification,
            PendingRegisteration
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
                Cosmos = new CosmosMethods(cloudLogin.Options.Cosmos.ConnectionString, cloudLogin.Options.Cosmos.DatabaseId, cloudLogin.Options.Cosmos.ContainerId);
        }

        private async Task OnNextClicked(MouseEventArgs e)
        {
            //user as put the email = > check if exists
            if (string.IsNullOrEmpty(Value))
                return;

            State = ProcessEvent.PengindLoading;

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
                    StateHasChanged();
                }
            }
            else//user doesn't exist => go to registration
            {
                Providers = cloudLogin.Options.Providers.Select(key => new Provider(key.Code)).ToList();
                State = ProcessEvent.PendingProviders;
                StateHasChanged();
            }
        }

        private async Task OnBackClicked(MouseEventArgs e)
        {
            WrongCode = false;
            State = ProcessEvent.PendingEmail;
        }
        private async Task OnNewCodeClicked(MouseEventArgs e)
        {
            State = ProcessEvent.PengindLoading;
            ExpiredCode = false;
            VerificationCode = CreateRandomCode(6);
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
                                State = ProcessEvent.PengindLoading;
                                VerificationCode = CreateRandomCode(6);//create code

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
                else //user doesn't exist => create instance in db => IsRegistered = false/IsVerified = false/IsLocked = false => Create code
                {
                    VerificationCode = CreateRandomCode(6);//create code
                    DebugCodeShow = VerificationCode;
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
                    State = ProcessEvent.PengindLoading;
                    await Cosmos.Container.CreateItemAsync(user);//create instance in db ..
                    State = ProcessEvent.PendingVerification; //go to verification
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
                {
                    if (VerificationValue == email.Code)
                    {//Code is right => check if expired
                     //Checking if code is expired
                        DateTimeOffset CheckTime = email.VerificationCodeTime.Value;
                        if (DateTimeOffset.UtcNow < CheckTime.AddSeconds(30))//30 seconds expiration
                        {
                            //code is not expired
                            if (CheckUser?.IsRegistered == false)
                            {// user is not registered => Make code empty => Goto verified

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
                                State = ProcessEvent.PengindLoading;
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
                    {
                        WrongCode = true;
                    }
                }
            });
        }

        private async Task OnRegisterClicked(MouseEventArgs e)
        {
            //Register button is clicked => IsRegistered = true / IsVerified = true => Update user info => push to database => Login user
            if (FirstName == null || LastName == null || DisplayName == null)
            {//check if any of the input is empty
                EmptyInput = true;
            }
            else//all input has value
            {
                EmptyInput = false;
                State = ProcessEvent.PengindLoading;
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

                string id = $"User|{user.ID}";
                PartitionKey partitionKey = new PartitionKey(user.PartitionKey);
                await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);//push to db

                //login user
                NavigationManager.NavigateTo($"/cloudlogin/Custom/CustomLogin?userID={user.ID}&KeepMeSignedIn=false", true);
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
    }
}
