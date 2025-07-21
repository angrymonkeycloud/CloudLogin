using AngryMonkey.CloudLogin.Models;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AngryMonkey.CloudLogin.Services
{
    public class AuthenticationProcessService(NavigationManager navigationManager, Interfaces.ICloudLogin cloudLogin)
    {
        private readonly NavigationManager _navigationManager = navigationManager;
        private Interfaces.ICloudLogin _cloudLogin = cloudLogin;

        public AuthenticationProcess CurrentProcess { get; private set; } = AuthenticationProcess.None;
        public ProcessStep CurrentStep { get; private set; } = ProcessStep.None;

        public string Title { get; private set; } = string.Empty;
        public string Subtitle { get; private set; } = string.Empty;
        public bool DisplayInputValue { get; private set; } = false;
        public bool IsLoading { get; private set; } = false;
        public List<string> Errors { get; private set; } = [];

        public event Action OnStateChanged;

        public async Task InitializeProcess(AuthenticationProcess process, string actionState = "login")
        {
            CurrentProcess = process;

            // Map the process to the initial step
            CurrentStep = GetInitialStepForProcess(process);

            await SwitchStep(CurrentStep);

            NotifyStateChanged();
        }

        private ProcessStep GetInitialStepForProcess(AuthenticationProcess process)
        {
            return process switch
            {
                AuthenticationProcess.StandardLogin => ProcessStep.InputValue,
                AuthenticationProcess.PasswordLogin => ProcessStep.InputValue,
                AuthenticationProcess.OtpLogin => ProcessStep.InputValue,
                AuthenticationProcess.Registration => ProcessStep.InputValue,
                AuthenticationProcess.PasswordReset => ProcessStep.InputValue,
                AuthenticationProcess.UpdateAccount => ProcessStep.Registration,
                AuthenticationProcess.AddInput => ProcessStep.InputValue,
                AuthenticationProcess.ChangePrimary => ProcessStep.ChangePrimary,
                _ => ProcessStep.InputValue
            };
        }

        public async Task NextStep()
        {
            ProcessStep nextStep = GetNextStep(CurrentProcess, CurrentStep);

            if (nextStep != CurrentStep)
                await SwitchStep(nextStep);

            NotifyStateChanged();
        }

        public async Task PreviousStep()
        {
            ProcessStep previousStep = GetPreviousStep(CurrentProcess, CurrentStep);

            if (previousStep != CurrentStep)
                await SwitchStep(previousStep);

            NotifyStateChanged();
        }

        public async Task SwitchStep(ProcessStep step)
        {
            if (step == CurrentStep)
            {
                ResetCurrentStep();
                NotifyStateChanged();
                return;
            }

            CurrentStep = step;
            UpdateStepMetadata(step);
            NotifyStateChanged();
        }

        private void UpdateStepMetadata(ProcessStep step)
        {
            switch (step)
            {
                case ProcessStep.InputValue:
                    Title = "Sign in";
                    Subtitle = string.Empty;
                    DisplayInputValue = false;

                    if (CurrentProcess == AuthenticationProcess.AddInput)
                    {
                        Title = "Add Input";
                        Subtitle = "Add another input for your account";
                    }
                    break;

                case ProcessStep.Providers:
                    Title = "Continue signing in";
                    Subtitle = "Sign In with";
                    DisplayInputValue = true;

                    if (CurrentProcess == AuthenticationProcess.AddInput)
                        Title = "Continue adding input";
                    break;

                case ProcessStep.CodeVerification:
                case ProcessStep.CodeEmailVerification:
                    string inputType = "Email"; // This would be dynamic based on input
                    Title = $"Verify your {inputType}";
                    Subtitle = $"A verification code has been sent to your {inputType}, if not received, you can send another one.";
                    DisplayInputValue = true;
                    break;

                case ProcessStep.Registration:
                    if (CurrentProcess == AuthenticationProcess.UpdateAccount)
                    {
                        Title = "Update";
                        Subtitle = "Change your credentials.";
                    }
                    else
                    {
                        Title = "Register";
                        Subtitle = "Add your credentials.";
                    }
                    DisplayInputValue = true;
                    break;

                case ProcessStep.EmailPasswordLogin:
                    Title = "Sign In";
                    break;

                case ProcessStep.EmailPasswordRegister:
                    Title = "Register";
                    break;

                case ProcessStep.EmailForgetPassword:
                    Title = "Forget Password";
                    break;

                case ProcessStep.ChangePrimary:
                    Title = "Set Primary";
                    Subtitle = "Choose which email to put as primary.";
                    DisplayInputValue = true;
                    break;

                default:
                    Title = "Untitled!!!";
                    Subtitle = string.Empty;
                    DisplayInputValue = false;
                    break;
            }
        }

        private void ResetCurrentStep()
        {
            switch (CurrentProcess)
            {
                case AuthenticationProcess.StandardLogin:
                case AuthenticationProcess.PasswordLogin:
                case AuthenticationProcess.OtpLogin:
                    Title = "Sign In";
                    Subtitle = string.Empty;
                    DisplayInputValue = false;
                    break;

                case AuthenticationProcess.AddInput:
                    Title = "Add Input";
                    Subtitle = "Add another input for your account";
                    break;

                    // Add other process types as needed
            }
        }

        private ProcessStep GetNextStep(AuthenticationProcess process, ProcessStep currentStep)
        {
            // Define transitions for each process type
            if (process == AuthenticationProcess.StandardLogin)
            {
                return currentStep switch
                {
                    ProcessStep.InputValue => ProcessStep.Providers,
                    ProcessStep.Providers => ProcessStep.CodeVerification,
                    ProcessStep.CodeVerification => ProcessStep.Registration,
                    _ => currentStep
                };
            }
            else if (process == AuthenticationProcess.PasswordLogin)
            {
                return currentStep switch
                {
                    ProcessStep.InputValue => ProcessStep.EmailPasswordLogin,
                    _ => currentStep
                };
            }
            else if (process == AuthenticationProcess.PasswordReset)
            {
                return currentStep switch
                {
                    ProcessStep.InputValue => ProcessStep.EmailForgetPassword,
                    ProcessStep.EmailForgetPassword => ProcessStep.CodeVerification,
                    _ => currentStep
                };
            }

            // Add other process flows as needed

            return currentStep;
        }

        private ProcessStep GetPreviousStep(AuthenticationProcess process, ProcessStep currentStep)
        {
            // Define backward transitions for each process type
            if (process == AuthenticationProcess.StandardLogin)
            {
                return currentStep switch
                {
                    ProcessStep.Providers => ProcessStep.InputValue,
                    ProcessStep.CodeVerification => ProcessStep.Providers,
                    ProcessStep.Registration => ProcessStep.CodeVerification,
                    _ => currentStep
                };
            }
            else if (process == AuthenticationProcess.PasswordLogin)
            {
                return currentStep switch
                {
                    ProcessStep.EmailPasswordLogin => ProcessStep.InputValue,
                    _ => currentStep
                };
            }
            else if (process == AuthenticationProcess.PasswordReset)
            {
                return currentStep switch
                {
                    ProcessStep.EmailForgetPassword => ProcessStep.InputValue,
                    ProcessStep.CodeVerification => ProcessStep.EmailForgetPassword,
                    _ => currentStep
                };
            }

            // Add other process flows as needed

            return currentStep;
        }

        public void StartLoading()
        {
            IsLoading = true;
            Errors.Clear();
            NotifyStateChanged();
        }

        public void EndLoading()
        {
            IsLoading = false;
            NotifyStateChanged();
        }

        public void AddError(string error)
        {
            Errors.Add(error);
            NotifyStateChanged();
        }

        public void ClearErrors()
        {
            Errors.Clear();
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}