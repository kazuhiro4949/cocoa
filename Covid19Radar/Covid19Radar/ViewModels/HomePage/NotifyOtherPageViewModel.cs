﻿/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Covid19Radar.Services;
using Covid19Radar.Services.Logs;
using Prism.Navigation;
using Xamarin.Forms;
using System;
using Acr.UserDialogs;
using Covid19Radar.Views;
using System.Text.RegularExpressions;
using Covid19Radar.Common;
using Covid19Radar.Resources;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using Chino;
using System.Net;
using System.Linq;

namespace Covid19Radar.ViewModels
{
    public class NotifyOtherPageViewModel : ViewModelBase, IExposureNotificationEventCallback
    {
        private readonly ILoggerService loggerService;
        private readonly ICloseApplication closeApplication;
        private readonly AbsExposureNotificationApiService exposureNotificationApiService;
        private readonly IDiagnosisKeyRegisterServer diagnosisKeyRegisterServer;

        private string _diagnosisUid;
        public string DiagnosisUid
        {
            get { return _diagnosisUid; }
            set
            {
                SetProperty(ref _diagnosisUid, value);
                IsEnabled = CheckRegisterButtonEnable();
            }
        }
        private bool _isEnabled;
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set { SetProperty(ref _isEnabled, value); }
        }
        private bool _isVisibleWithSymptomsLayout;
        public bool IsVisibleWithSymptomsLayout
        {
            get { return _isVisibleWithSymptomsLayout; }
            set
            {
                SetProperty(ref _isVisibleWithSymptomsLayout, value);
                IsEnabled = CheckRegisterButtonEnable();
            }
        }
        private bool _isVisibleNoSymptomsLayout;
        public bool IsVisibleNoSymptomsLayout
        {
            get { return _isVisibleNoSymptomsLayout; }
            set
            {
                SetProperty(ref _isVisibleNoSymptomsLayout, value);
                IsEnabled = CheckRegisterButtonEnable();
            }
        }
        private DateTime _diagnosisDate;
        public DateTime DiagnosisDate
        {
            get { return _diagnosisDate; }
            set { SetProperty(ref _diagnosisDate, value); }
        }
        private int errorCount { get; set; }

        public NotifyOtherPageViewModel(
            INavigationService navigationService,
            ILoggerService loggerService,
            ICloseApplication closeApplication,
            AbsExposureNotificationApiService exposureNotificationApiService,
            IDiagnosisKeyRegisterServer diagnosisKeyRegisterServer
            ) : base(navigationService)
        {
            Title = AppResources.TitileUserStatusSettings;

            this.loggerService = loggerService;
            this.closeApplication = closeApplication;
            this.exposureNotificationApiService = exposureNotificationApiService;
            this.diagnosisKeyRegisterServer = diagnosisKeyRegisterServer;

            errorCount = 0;
            DiagnosisUid = "";
            DiagnosisDate = DateTime.Today;
        }

        public Command OnClickRegister => (new Command(async () =>
        {
            loggerService.StartMethod();

            var result = await UserDialogs.Instance.ConfirmAsync(
                AppResources.NotifyOtherPageDiag1Message,
                AppResources.NotifyOtherPageDiag1Title,
                AppResources.ButtonRegister,
                AppResources.ButtonCancel);
            if (!result)
            {
                await UserDialogs.Instance.AlertAsync(
                    "",
                    AppResources.NotifyOtherPageDiag2Title,
                    AppResources.ButtonOk
                    );

                loggerService.Info($"Canceled by user.");
                loggerService.EndMethod();
                return;
            }

            // Check helthcare authority positive api check here!!
            try
            {
                if (errorCount >= AppConstants.MaxErrorCount)
                {
                    await UserDialogs.Instance.AlertAsync(
                        AppResources.NotifyOtherPageDiagAppClose,
                        AppResources.NotifyOtherPageDiagAppCloseTitle,
                        AppResources.ButtonOk
                    );
                    closeApplication.closeApplication();
                    loggerService.Error($"Exceeded the number of trials.");
                    return;
                }

                loggerService.Info($"Number of attempts to submit diagnostic number. ({errorCount + 1} of {AppConstants.MaxErrorCount})");

                if (errorCount > 0)
                {
                    await UserDialogs.Instance.AlertAsync(AppResources.NotifyOtherPageDiag3Message,
                        AppResources.NotifyOtherPageDiag3Title,
                        AppResources.ButtonOk
                        );
                    await Task.Delay(errorCount * 5000);
                }


                // Init Dialog
                if (string.IsNullOrEmpty(_diagnosisUid))
                {
                    await UserDialogs.Instance.AlertAsync(
                        AppResources.NotifyOtherPageDiag4Message,
                        AppResources.ProcessingNumberErrorDiagTitle,
                        AppResources.ButtonOk
                    );
                    errorCount++;
                    loggerService.Error($"No diagnostic number entered.");
                    return;
                }

                Regex regex = new Regex(AppConstants.positiveRegex);
                if (!regex.IsMatch(_diagnosisUid))
                {
                    await UserDialogs.Instance.AlertAsync(
                        AppResources.NotifyOtherPageDiag5Message,
                        AppResources.ProcessingNumberErrorDiagTitle,
                        AppResources.ButtonOk
                    );
                    errorCount++;
                    loggerService.Error($"Incorrect diagnostic number format.");
                    return;
                }

                // EN Enabled Check
                var enabled = await exposureNotificationApiService.IsEnabledAsync();

                if (!enabled)
                {
                    await UserDialogs.Instance.AlertAsync(
                       AppResources.NotifyOtherPageDiag6Message,
                       AppResources.NotifyOtherPageDiag6Title,
                       AppResources.ButtonOk
                    );
                    UserDialogs.Instance.HideLoading();
                    await NavigationService.NavigateAsync("/" + nameof(MenuPage) + "/" + nameof(NavigationPage) + "/" + nameof(HomePage));

                    loggerService.Warning($"Exposure notification is disable.");
                    return;
                }

                UserDialogs.Instance.ShowLoading(AppResources.LoadingTextRegistering);

                await SubmitDiagnosisKeys();

                UserDialogs.Instance.HideLoading();
            }
            catch (InvalidDataException ex)
            {
                UserDialogs.Instance.HideLoading();

                errorCount++;
                UserDialogs.Instance.Alert(
                    AppResources.NotifyOtherPageDialogExceptionTargetDiagKeyNotFound,
                    AppResources.NotifyOtherPageDialogExceptionTargetDiagKeyNotFoundTitle,
                    AppResources.ButtonOk
                );
                loggerService.Exception("Failed to submit UID invalid data.", ex);
            }
            catch (Exception ex)
            {
                UserDialogs.Instance.HideLoading();

                errorCount++;
                UserDialogs.Instance.Alert(
                    AppResources.NotifyOtherPageDialogExceptionText,
                    AppResources.NotifyOtherPageDialogExceptionTitle,
                    AppResources.ButtonOk
                );
                loggerService.Exception("Failed to submit UID.", ex);
            }
            finally
            {
                loggerService.EndMethod();
            }
        }));

        private async Task SubmitDiagnosisKeys()
        {
            loggerService.Info($"Submit the processing number.");

            try
            {
                List<TemporaryExposureKey> temporaryExposureKeyList
                    = await exposureNotificationApiService.GetTemporaryExposureKeyHistoryAsync();

                loggerService.Info($"TemporaryExposureKeys-count: {temporaryExposureKeyList.Count()}");

                IList<TemporaryExposureKey> filteredTemporaryExposureKeyList
                    = TemporaryExposureKeyUtils.FiilterTemporaryExposureKeys(
                        temporaryExposureKeyList,
                        _diagnosisDate,
                        AppConstants.DaysToSendTek,
                        loggerService
                        );

                loggerService.Info($"FilteredTemporaryExposureKeys-count: {filteredTemporaryExposureKeyList.Count()}");

                HttpStatusCode httpStatusCode = await diagnosisKeyRegisterServer.SubmitDiagnosisKeysAsync(
                    filteredTemporaryExposureKeyList,
                    _diagnosisUid
                    );
                loggerService.Info($"HTTP status is {httpStatusCode}({(int)httpStatusCode}).");

                ShowResult(httpStatusCode);
            }
            catch (ENException exception)
            {
                loggerService.Exception("GetTemporaryExposureKeyHistoryAsync", exception);
            }
            catch (Exception exception)
            {
                loggerService.Exception("SubmitDiagnosisKeys", exception);
            }
            finally
            {
                UserDialogs.Instance.HideLoading();
            }
        }

        private async void ShowResult(HttpStatusCode httpStatusCode)
        {
            switch (httpStatusCode)
            {
                case HttpStatusCode.NoContent:
                    // Success
                    loggerService.Info($"Successfully submit the diagnostic number.");

                    await UserDialogs.Instance.AlertAsync(
                        "",
                        AppResources.NotifyOtherPageDialogSubmittedTitle,
                        AppResources.ButtonOk
                    );
                    await NavigationService.NavigateAsync("/" + nameof(MenuPage) + "/" + nameof(NavigationPage) + "/" + nameof(HomePage));
                    break;

                case HttpStatusCode.NotAcceptable:
                    await UserDialogs.Instance.AlertAsync(
                        AppResources.ExposureNotificationHandler1ErrorMessage,
                        AppResources.ProcessingNumberErrorDiagTitle,
                        AppResources.ButtonOk);
                    loggerService.Error($"The process number is incorrect.");
                    break;

                case HttpStatusCode.InternalServerError:
                case HttpStatusCode.ServiceUnavailable:
                    await UserDialogs.Instance.AlertAsync(
                        "",
                        AppResources.ExposureNotificationHandler2ErrorMessage,
                        AppResources.ButtonOk);
                    loggerService.Error($"Cannot connect to the service.");
                    break;

                case HttpStatusCode.BadRequest:
                    await UserDialogs.Instance.AlertAsync(
                        "",
                        AppResources.ExposureNotificationHandler3ErrorMessage,
                        AppResources.ButtonOk);
                    loggerService.Error($"There is a problem with the record data.");
                    break;

                default:
                    loggerService.Error($"Unexpected status");
                    break;
            }
        }

        public void OnClickRadioButtonIsTrueCommand(string text)
        {
            loggerService.StartMethod();

            if (AppResources.NotifyOtherPageRadioButtonYes.Equals(text))
            {
                IsVisibleWithSymptomsLayout = true;
                IsVisibleNoSymptomsLayout = false;
            }
            else if (AppResources.NotifyOtherPageRadioButtonNo.Equals(text))
            {
                IsVisibleWithSymptomsLayout = false;
                IsVisibleNoSymptomsLayout = true;
            }
            else
            {
                IsVisibleWithSymptomsLayout = false;
                IsVisibleNoSymptomsLayout = false;
            }

            loggerService.Info($"Is visible with symptoms layout: {IsVisibleWithSymptomsLayout}, Is visible no symptoms layout: {IsVisibleNoSymptomsLayout}");
            loggerService.EndMethod();
        }

        public bool CheckRegisterButtonEnable()
        {
            return DiagnosisUid.Length == AppConstants.MaxDiagnosisUidCount && (IsVisibleWithSymptomsLayout || IsVisibleNoSymptomsLayout);
        }

        public async void OnGetTekHistoryAllowed()
        {
            loggerService.StartMethod();

            await SubmitDiagnosisKeys();

            loggerService.EndMethod();
        }
    }
}
