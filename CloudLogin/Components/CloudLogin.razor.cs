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

namespace AngryMonkey.Cloud.Login
{
    public partial class CloudLogin
    {
        [Parameter] public string Value { get; set; }
        [Parameter] public string FirstName { get; set; }
        [Parameter] public string LastName { get; set; }
        [Parameter] public string VerificationValue { get; set; }
        [Parameter] public bool KeepMeSignedIn { get; set; }
        [Parameter] public bool WrongCode { get; set; } = false;
        [Parameter] public string VerificationCode { get; set; }
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
            State = ProcessEvent.PendingEmail;
        }
        private void OnProviderClicked(Provider provider)
        {
            //if (provider.Name.ToLower() == "email")
            //    NavigationManager.NavigateTo($"/cloudlogin/login/CustomLogin?user={user}", true);
            //else
            NavigationManager.NavigateTo($"/cloudlogin/login/{provider.Code}?emailaddress={Value}&redirectUri=/&KeepMeSignedIn={KeepMeSignedIn}", true);
        }
        private async Task OnVerifyClicked(MouseEventArgs e)
        {

            if (VerificationValue == VerificationCode)
                State = ProcessEvent.PendingVerified;
            else
                WrongCode = true;
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
            Cosmos.Container.PatchItemAsync<dynamic>(id, partitionKey, patchOperations);

            //login the user
        }



        private async Task CustomEmailLoginAsync()
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
                                    Provider = "Custom",
                                    VerificationCode = VerificationCode
                                }
                            }
            };
            Cosmos.Container.CreateItemAsync(user);

            State = ProcessEvent.PendingVerification;
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
                "email" => "Email",
                _ => Code
            };
        }
    }
}
