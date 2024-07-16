﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Runtime.InteropServices;
using DevHome.Common.Extensions;
using DevHome.Common.Services;
using Microsoft.UI.Xaml;
using Serilog;
using Windows.ApplicationModel;
using Windows.Wdk.System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using PInvokeWdk = Windows.Wdk.PInvoke;

namespace DevHome.PI.Helpers;

internal sealed class CommonHelper
{
    public const string UnpinGlyph = "\uE77A";
    public const string PinGlyph = "\uE718";

    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(CommonHelper));

    internal static string GetLocalizedString(string stringName, params object[] args)
    {
        var stringResource = new StringResource();
        var localizedString = stringResource.GetLocalized(stringName, args);
        Debug.Assert(!string.IsNullOrEmpty(localizedString), stringName + " is empty. Check if " + stringName + " is present in Resources.resw.");
        return localizedString;
    }

    internal static void RunAsAdmin(int pid, string pageName)
    {
        var startInfo = new ProcessStartInfo();
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;

        var aliasSubDirectoryPath = $"Microsoft\\WindowsApps\\{Package.Current.Id.FamilyName}\\devhome.pi.exe";
        var aliasPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), aliasSubDirectoryPath);
        startInfo.FileName = aliasPath;

        // Pass pid and the page from where the admin request came from
        startInfo.Arguments = $"--pid {pid} --expandWindow {pageName}";
        startInfo.UseShellExecute = true;
        startInfo.Verb = "runas";

        var process = new Process();
        process.StartInfo = startInfo;

        // Since a UAC prompt will be shown, we need to wait for the process to exit
        // This can also be cancelled by the user which will result in an exception
        try
        {
            process.Start();

            // Close the primary window for this instance and exit
            var primaryWindow = Application.Current.GetService<PrimaryWindow>();
            primaryWindow.Close();
        }
        catch (Win32Exception ex)
        {
            _log.Error(ex, "Could not run PI as admin");
            if (ex.NativeErrorCode == (int)WIN32_ERROR.ERROR_CANT_ACCESS_FILE)
            {
                var barWindow = Application.Current.GetService<PrimaryWindow>().DBarWindow;
                barWindow?.ShowDialogToEnableAppExecutionAlias();
            }
            else if (ex.NativeErrorCode == (int)WIN32_ERROR.ERROR_CANCELLED)
            {
                _log.Error(ex, "UAC to run PI as admin was denied");
            }
        }
    }

    public static unsafe int GetParentProcessId(Process process)
    {
        var pbi = default(PROCESS_BASIC_INFORMATION);
        int status = PInvokeWdk.NtQueryInformationProcess((HANDLE)process.Handle, PROCESSINFOCLASS.ProcessBasicInformation, &pbi, (uint)Marshal.SizeOf(pbi), null);
        if (status != 0)
        {
            throw new InvalidOperationException("Failed to query process information.");
        }

        return (int)pbi.InheritedFromUniqueProcessId;
    }

    public static HWND? TryGetParentProcessHWND()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var parentProcessId = GetParentProcessId(process);
            if (parentProcessId != 0)
            {
                using var parentProcess = Process.GetProcessById(parentProcessId);
                return new HWND(parentProcess.MainWindowHandle);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get parent process HWND");
        }

        return null;
    }
}
