using Newtonsoft.Json;
using AngryMonkey.Cloud.Login;
using Microsoft.AspNetCore.Components;
using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;
using CloudLoginDataContract;

namespace ServerClientDemo.Client.Pages
{
    public partial class Index
    {
        public CloudUser CurrentUser { get; set; } = new();
        public bool IsAuthorized { get; set; } = false;
        //private async Task DeleteButton() => await cloudLogin.DeleteUser(CurrentUser.ID);
        private async Task CheckUsername()
        {
            await cloudLogin.GetUsersByDisplayName("rami gerges");
        }

        protected override async Task OnInitializedAsync()
        {
            IsAuthorized = await cloudLogin.IsAuthenticated();
            CurrentUser = await cloudLogin.CurrentUser();
        }
        private string? ImportedPhoneNumber { get; set; }

        private async Task ImportNumber()
        {
            if (CurrentUser == null)
                return;
            if (ImportedPhoneNumber == null)
                return;

            CloudGeographyClient geographyClient = new();
            PhoneNumber numberSplitted = geographyClient.PhoneNumbers.Get(ImportedPhoneNumber);

            LoginInput Input = new LoginInput()
            {
                Input = numberSplitted.Number.Trim(),
                Format = InputFormat.PhoneNumber,
                PhoneNumberCountryCode = numberSplitted.CountryCode.Trim(),
                PhoneNumberCallingCode = numberSplitted.CountryCallingCode.Trim(),
                Providers = new()
                {
                    new LoginProvider()
                    {
                        Code = "Coverbox"
                    }
                }
            };

            //await cloudLogin.AddInput(CurrentUser.ID, Input);
        }
    }
}