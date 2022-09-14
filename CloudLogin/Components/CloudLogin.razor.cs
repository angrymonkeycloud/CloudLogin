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

namespace AngryMonkey.Cloud.Login
{
    public partial class CloudLogin
    {
        [Parameter] public string Value { get; set; }
        [Parameter] public string Valuetest { get; set; }
        [Parameter] public string FirstName { get; set; }
        [Parameter] public string LastName { get; set; }
        [Parameter] public string VerificationValue { get; set; }
        [Parameter] public bool KeepMeSignedIn { get; set; }
        [Parameter] public bool WrongCode { get; set; } = false;
        [Parameter] public bool ExpiredCode { get; set; } = false;
        [Parameter] public string VerificationCode { get; set; }
        [Parameter] public DateTimeOffset VerificationCodeTime { get; set; }
        internal CosmosMethods Cosmos { get; set; }
        List<Provider> Providers { get; set; } = new();

        protected bool EnableEmailAddressField
        {
            get
            {
                return State != ProcessEvent.PendingProviders;
            }
        }

        protected ProcessEvent State { get; set; } = ProcessEvent.PendingEmail;

        protected enum ProcessEvent
        {
            PendingEmail,
            PendingProviders,
            PendingAuthorization,
            PendingRegistration,
            PendingVerification,
            PendingVerified
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
                Cosmos = new CosmosMethods(cloudLogin.Options.Cosmos.ConnectionString, cloudLogin.Options.Cosmos.DatabaseId, cloudLogin.Options.Cosmos.ContainerId);
        }

        private async Task test(MouseEventArgs e)
        {

            //create a claim
            var claim1 = new Claim(ClaimTypes.NameIdentifier, Valuetest);
            var claim2 = new Claim(ClaimTypes.Name, Valuetest);
            var claim3 = new Claim(ClaimTypes.Surname, Valuetest);
            var claim4 = new Claim(ClaimTypes.Email, Valuetest);
            //create claimsIdentity
            var claimsIdentity = new ClaimsIdentity(new[] { claim1, claim2, claim3, claim4 }, "CloudLogin");
            //create claimsPrincipal
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            NavigationManager.NavigateTo($"/cloudlogin/Custom/CustomLogin?userID={Valuetest}&KeepMeSignedIn=false", true);

            //      NavigationManager.NavigateTo($"/cloudlogin/login/{provider.Code}?emiladdress={Value}&redirectUri=/&KeepMeSignedIn={KeepMeSignedIn}", true);

        }
        private async Task OnNextClicked(MouseEventArgs e)
        {
            if (string.IsNullOrEmpty(Value))
                return;

            State = ProcessEvent.PendingProviders;

            Value = Value.ToLower();

            AngryMonkey.Cloud.Login.DataContract.User? user = await Cosmos.GetUserByEmailAddress(Value);

            if (user != null)
            {
                Providers = user.Providers.Select(key => new Provider(key)).ToList();
                State = ProcessEvent.PendingAuthorization;
            }
            else
            {
                Providers = cloudLogin.Options.Providers.Select(key => new Provider(key.Code)).ToList();
                State = ProcessEvent.PendingRegistration;

            }
        }

        private async Task OnBackClicked(MouseEventArgs e)
        {
            WrongCode = false;
            State = ProcessEvent.PendingEmail;
        }
        private async Task OnNewCodeClicked(MouseEventArgs e)
        {
            ExpiredCode = false;
            string NewVerificationCode = CreateRandomCode(6);
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Replace("/EmailAddresses/0/VerificationCode",NewVerificationCode),
                PatchOperation.Replace("/EmailAddresses/0/VerificationCodeTime",DateTimeOffset.UtcNow)
            };

            AngryMonkey.Cloud.Login.DataContract.User? user = await Cosmos.GetUserByEmailAddress(Value);
            string id = $"User|{user.ID}";
            PartitionKey partitionKey = new PartitionKey(user.PartitionKey);
            await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);

            State = ProcessEvent.PendingVerification;
        }
        private async Task OnProviderClickedAsync(Provider provider)
        {
            if (provider.Code.ToLower() == "email")
            {

                AngryMonkey.Cloud.Login.DataContract.User? CheckUser = await Cosmos.GetUserByEmailAddress(Value);
                string CheckCode = CheckUser?.EmailAddresses?.FirstOrDefault()?.VerificationCode;
                VerificationCode = CheckCode;// to show verification code
                if (CheckCode == null)
                {
                    VerificationCode = CreateRandomCode(6);

                    //save verification code to cosmos
                    Guid CustomUserID = Guid.NewGuid();
                    AngryMonkey.Cloud.Login.DataContract.User user = new()
                    {
                        ID = CustomUserID,

                        EmailAddresses = new()
                            {
                                new UserEmailAddress()
                                {
                                    EmailAddress = Value,
                                    IsPrimary = true,
                                    ProviderId = CustomUserID.ToString(),
                                    Provider = "Email",
                                    VerificationCode = VerificationCode,
                                    VerificationCodeTime = DateTimeOffset.UtcNow
                                }
                            }
                    };
                    await Cosmos.Container.CreateItemAsync(user);
                    VerificationCode = CheckCode;//to show verification code
                    State = ProcessEvent.PendingVerification;

                }
                else
                {
                    State = ProcessEvent.PendingVerification;
                }
                //NavigationManager.NavigateTo($"/cloudlogin/login/CustomLogin?user={user}", true);
            }
            else
                NavigationManager.NavigateTo($"/cloudlogin/login/{provider.Code}?emailaddress={Value}&redirectUri=/&KeepMeSignedIn={KeepMeSignedIn}", true);
        }
        private async Task OnVerifyClicked(MouseEventArgs e)
        {
            AngryMonkey.Cloud.Login.DataContract.User? CheckUser = await Cosmos.GetUserByEmailAddress(Value);

            if (VerificationValue == CheckUser.EmailAddresses.FirstOrDefault().VerificationCode)
            {
                DateTimeOffset CheckTime = CheckUser.EmailAddresses.FirstOrDefault().VerificationCodeTime.Value;



                if (DateTimeOffset.UtcNow < CheckTime.AddSeconds(30))
                {
                    State = ProcessEvent.PendingVerified;
                }
                else
                {
                    VerificationValue = "";
                    WrongCode = false;
                    ExpiredCode = true;

                }
            }
            else
            {
                VerificationValue = "";
                WrongCode = true;
            }
        }

        private async Task OnRegisterClicked(MouseEventArgs e)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Add("/FirstName", FirstName),
                PatchOperation.Add("/LastName", LastName),
                PatchOperation.Add("/DisplayName", $"{FirstName} {LastName}"),
                PatchOperation.Add("/LastSignedIn", DateTimeOffset.UtcNow)
            };


            AngryMonkey.Cloud.Login.DataContract.User? user = await Cosmos.GetUserByEmailAddress(Value);
            string id = $"User|{user.ID}";
            PartitionKey partitionKey = new PartitionKey(user.PartitionKey);
            await Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);

            //login the userAuthenticationProperties properties = new()
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
    }
}
