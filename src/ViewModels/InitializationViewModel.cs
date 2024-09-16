﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.ComponentModel;
using DevHome.Common.Extensions;
using DevHome.Common.Helpers;
using DevHome.Contracts.Services;
using DevHome.Dashboard.Services;
using DevHome.Services.Core.Contracts;
using DevHome.Telemetry;
using DevHome.TelemetryEvents;
using DevHome.Views;
using Microsoft.UI.Xaml;
using Serilog;

namespace DevHome.ViewModels;

public class InitializationViewModel : ObservableObject
{
    private readonly ILogger _log = Log.ForContext("SourceContext", nameof(InitializationViewModel));

    private readonly IThemeSelectorService _themeSelector;
    private readonly IWidgetServiceService _widgetServiceService;
    private readonly IMicrosoftStoreService _msStoreService;
    private readonly IPackageDeploymentService _packageDeploymentService;

#if CANARY_BUILD
    private const string GitHubExtensionStorePackageId = "9N806ZKPW85R";
    private const string GitHubExtensionPackageFamilyName = "Microsoft.Windows.DevHomeGitHubExtension.Canary_8wekyb3d8bbwe";
#elif STABLE_BUILD
    private const string GitHubExtensionStorePackageId = "9NZCC27PR6N6";
    private const string GitHubExtensionPackageFamilyName = "Microsoft.Windows.DevHomeGitHubExtension_8wekyb3d8bbwe";
#else
    private const string GitHubExtensionStorePackageId = "";
    private const string GitHubExtensionPackageFamilyName = "";
#endif

    public InitializationViewModel(
        IThemeSelectorService themeSelector,
        IWidgetServiceService widgetServiceService,
        IMicrosoftStoreService msStoreService,
        IPackageDeploymentService packageDeploymentService)
    {
        _themeSelector = themeSelector;
        _widgetServiceService = widgetServiceService;
        _msStoreService = msStoreService;
        _packageDeploymentService = packageDeploymentService;
    }

    public async void OnPageLoaded()
    {
        TelemetryFactory.Get<ITelemetry>().Log("DevHome_Initialization_Started_Event", LogLevel.Critical, new DevHomeInitializationStartedEvent());
        _log.Information("Dev Home Initialization starting.");

        // Install the widget service if we're on Windows 10 and it's not already installed.
        try
        {
            var widgetStatus = _widgetServiceService.GetWidgetServiceState();
            if (widgetStatus != WidgetServiceService.WidgetServiceStates.NotAtMinVersion)
            {
                _log.Information("Skipping installing WidgetService, already installed.");
            }
            else
            {
                if (!RuntimeHelper.IsOnWindows11)
                {
                    // We're on Windows 10 and don't have the widget service, try to install it.
                    await _widgetServiceService.TryInstallingWidgetService();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Information(ex, "Installing WidgetService failed: ");
        }

        // Install the DevHomeGitHubExtension, unless it's already installed or a dev build is running.
        if (string.IsNullOrEmpty(GitHubExtensionStorePackageId) || HasDevHomeGitHubExtensionInstalled())
        {
            _log.Information("Skipping installing DevHomeGitHubExtension.");
        }
        else
        {
            try
            {
                _log.Information("Installing DevHomeGitHubExtension...");
                await _msStoreService.TryInstallPackageAsync(GitHubExtensionStorePackageId);
            }
            catch (Exception ex)
            {
                _log.Information(ex, "Installing DevHomeGitHubExtension failed: ");
            }
        }

        App.MainWindow.Content = Application.Current.GetService<ShellPage>();

        _themeSelector.SetRequestedTheme();

        TelemetryFactory.Get<ITelemetry>().Log("DevHome_Initialization_Ended_Event", LogLevel.Critical, new DevHomeInitializationEndedEvent());
        _log.Information("Dev Home Initialization ended.");
    }

    private bool HasDevHomeGitHubExtensionInstalled()
    {
        var packages = _packageDeploymentService.FindPackagesForCurrentUser(GitHubExtensionPackageFamilyName);

        // Don't check here if the package is ok, we'll do that later on the Dashboard.
        return packages.Any();
    }
}
