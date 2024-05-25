﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using DevHome.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.UI.Dispatching;
using Serilog;

namespace DevHome;

public static class Program
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(Program));
    private static App? _app;

    [STAThread]
    public static void Main(string[] args)
    {
        // Be sure to parse these args in this instance of the exe... don't redirect this to another instance for parsing which
        // may be running in a different security context.
        ParseCommandLine(args);

        WinRT.ComWrappersSupport.InitializeComWrappers();

        var isRedirect = DecideRedirection().GetAwaiter().GetResult();

        if (!isRedirect)
        {
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                var context = new DispatcherQueueSynchronizationContext(dispatcherQueue);
                SynchronizationContext.SetSynchronizationContext(context);
                _app = new App();
            });
        }
    }

    private static async Task<bool> DecideRedirection()
    {
        var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("main");
        var activatedEventArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();

        var isRedirect = false;
        if (!mainInstance.IsCurrent)
        {
            // Redirect the activation (and args) to the "main" instance, and exit.
            await mainInstance.RedirectActivationToAsync(activatedEventArgs);
            isRedirect = true;
        }

        return isRedirect;
    }

    // Currently DevHome supports one set of command line arguments, most useful when debugging different apps within the Dev Home package.
    //
    // For example:
    //    --utilitylaunch DevHome.PI.Exe --utilityLaunchArgs "--application problemapp2"
    //
    // --utilityLaunch is the name of the utility to launch
    // --utilityLaunchArgs are the arguments to pass to the utility. This is optional, but be sure to include the quotes if you have spaces in the arguments.
    private static void ParseCommandLine(string[] args)
    {
        var builder = new ConfigurationBuilder();
        builder.AddCommandLine(args);
        var config = builder.Build();

        var utilityToLaunch = config["utilitylaunch"];
        var utilityLaunchArgs = config["utilitylaunchargs"];

        if (!string.IsNullOrEmpty(utilityToLaunch))
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = utilityToLaunch,
                    Arguments = utilityLaunchArgs,
                };

                Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Error launching utility: {ex.Message}");
            }
        }
    }
}
