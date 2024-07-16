﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHome.Common.Helpers;
using DevHome.PI.Helpers;
using DevHome.PI.Models;
using Microsoft.UI.Xaml;
using Serilog;
using Windows.Win32;

namespace DevHome.PI.ViewModels;

public partial class AppDetailsPageViewModel : ObservableObject
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AppDetailsPageViewModel));

    [ObservableProperty]
    private AppRuntimeInfo _appInfo;

    [ObservableProperty]
    private Visibility _runAsAdminVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _processRunningParamsVisibility = Visibility.Collapsed;

    private Process? _targetProcess;

    public AppDetailsPageViewModel()
    {
        TargetAppData.Instance.PropertyChanged += TargetApp_PropertyChanged;
        AppInfo = new();

        var process = TargetAppData.Instance.TargetProcess;
        if (process is not null)
        {
            UpdateTargetProcess(process);
        }
    }

    public void UpdateTargetProcess(Process process)
    {
        if (_targetProcess != process)
        {
            _targetProcess = process;
            RunAsAdminVisibility = Visibility.Collapsed;
            AppInfo = new();

            try
            {
                AppInfo.ProcessId = _targetProcess.Id;

                if (process.HasExited)
                {
                    AppInfo.Visibility = Visibility.Collapsed;
                    ProcessRunningParamsVisibility = Visibility.Collapsed;
                }
                else
                {
                    AppInfo.Visibility = Visibility.Visible;
                    ProcessRunningParamsVisibility = Visibility.Visible;
                    AppInfo.IsRunningAsSystem = TargetAppData.Instance.IsRunningAsSystem;
                    AppInfo.IsRunningAsAdmin = TargetAppData.Instance.IsRunningAsAdmin;
                    AppInfo.BasePriority = _targetProcess.BasePriority;
                    AppInfo.PriorityClass = (int)_targetProcess.PriorityClass;

                    if (_targetProcess.MainModule != null)
                    {
                        AppInfo.MainModuleFileName = _targetProcess.MainModule.FileName;
                        var cpuArchitecture = WindowHelper.GetAppArchitecture(
                            _targetProcess.SafeHandle, _targetProcess.MainModule.FileName);
                        AppInfo.CpuArchitecture = cpuArchitecture;
                    }

                    foreach (ProcessModule module in _targetProcess.Modules)
                    {
                        AppInfo.CheckFrameworkTypes(module.ModuleName);
                    }

                    AppInfo.IsStoreApp = PInvoke.IsImmersiveProcess(_targetProcess.SafeHandle);
                }
            }
            catch (Win32Exception ex)
            {
                // This can throw if the process is running elevated and we are not.
                _log.Error(ex, "Unable to construct an AppInfo for target process.");
                if (ex.NativeErrorCode == (int)Windows.Win32.Foundation.WIN32_ERROR.ERROR_ACCESS_DENIED)
                {
                    // Hide properties that cannot be retrieved when the target app is elevated and PI is not.
                    AppInfo.Visibility = Visibility.Collapsed;

                    // Only show the button when not running as admin. This is possible when the target app is a system app.
                    if (!RuntimeHelper.IsCurrentProcessRunningAsAdmin())
                    {
                        RunAsAdminVisibility = Visibility.Visible;
                    }
                }
            }
        }
    }

    private void TargetApp_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetAppData.TargetProcess))
        {
            if (TargetAppData.Instance.TargetProcess is not null)
            {
                UpdateTargetProcess(TargetAppData.Instance.TargetProcess);
            }
        }
    }

    [RelayCommand]
    private void RunAsAdmin()
    {
        if (_targetProcess is not null)
        {
            CommonHelper.RunAsAdmin(_targetProcess.Id, nameof(AppDetailsPageViewModel));
        }
    }
}
