﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Windows.DevHome.SDK;
using Serilog;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Foundation;
using WindowsSandboxExtension.Helpers;
using WindowsSandboxExtension.Telemetry;

using Timer = System.Timers.Timer;

namespace WindowsSandboxExtension.Providers;

public class WindowsSandboxComputeSystem : IComputeSystem, IDisposable
{
    private const long ByteSizeGB = 1024 * 1024 * 1024;
    private const long DefaultMemorySizeInBytes = 4 * ByteSizeGB;
    private const long DefaultStorageSizeInBytes = 80 * ByteSizeGB;

    private readonly Guid _id = Guid.NewGuid();
    private readonly ILogger _log = Log.ForContext("SourceContext", nameof(WindowsSandboxProvider));
    private readonly object _windowsSandboxStartLock = new();

    private Process? _windowsSandboxExeProcess;
    private ComputeSystemState _state = ComputeSystemState.Stopped;

    private ComputeSystemState State
    {
        get => _state;

        set
        {
            _state = value;
            StateChanged?.Invoke(this, value);
        }
    }

    public string AssociatedProviderId => Constants.ProviderId;

    public string DisplayName => Resources.GetResource("WindowsSandboxDisplayName", _log);

    public string Id => _id.ToString();

    public string SupplementalDisplayName => string.Empty;

    public ComputeSystemOperations SupportedOperations => State switch
    {
        ComputeSystemState.Running => ComputeSystemOperations.Terminate,
        _ => ComputeSystemOperations.None,
    };

    public IDeveloperId? AssociatedDeveloperId => null;

    public event TypedEventHandler<IComputeSystem, ComputeSystemState>? StateChanged;

    public IAsyncOperation<ComputeSystemThumbnailResult> GetComputeSystemThumbnailAsync(string options)
    {
        return Task.Run(async () =>
        {
            var uri = new Uri(Constants.Thumbnail);
            var storageFile = await StorageFile.GetFileFromApplicationUriAsync(uri);
            var randomAccessStream = await storageFile.OpenReadAsync();

            // Convert the stream to a byte array
            var bytes = new byte[randomAccessStream.Size];
            await randomAccessStream.ReadAsync(bytes.AsBuffer(), (uint)randomAccessStream.Size, InputStreamOptions.None);
            return new ComputeSystemThumbnailResult(bytes);
        }).AsAsyncOperation();
    }

    public IAsyncOperation<IEnumerable<ComputeSystemProperty>> GetComputeSystemPropertiesAsync(string options)
    {
        return Task.Run(() =>
        {
            var properties = new List<ComputeSystemProperty>
            {
                ComputeSystemProperty.Create(ComputeSystemPropertyKind.CpuCount, Environment.ProcessorCount),
                ComputeSystemProperty.Create(ComputeSystemPropertyKind.AssignedMemorySizeInBytes, DefaultMemorySizeInBytes),
                ComputeSystemProperty.Create(ComputeSystemPropertyKind.StorageSizeInBytes, DefaultStorageSizeInBytes),
            };

            return properties.AsEnumerable();
        }).AsAsyncOperation();
    }

    public IAsyncOperation<ComputeSystemStateResult> GetStateAsync()
    {
        return Task.Run(() =>
        {
            return new ComputeSystemStateResult(State);
        }).AsAsyncOperation();
    }

    public IAsyncOperation<ComputeSystemOperationResult> ConnectAsync(string options)
    {
        return Task.Run(() =>
        {
            try
            {
                // Windows Sandbox is not running.
                if (_windowsSandboxExeProcess == null || _windowsSandboxExeProcess.HasExited)
                {
                    State = ComputeSystemState.Starting;

                    var system32Path = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    var windowsSandboxExePath = Path.Combine(system32Path, Constants.WindowsSandboxExe);

                    _windowsSandboxExeProcess = new();
                    _windowsSandboxExeProcess.StartInfo.FileName = windowsSandboxExePath;
                    _windowsSandboxExeProcess.EnableRaisingEvents = true;
                    _windowsSandboxExeProcess.Exited += WindowsSandboxProcessExited;

                    State = ComputeSystemState.Running;
                    TraceLogging.StartingWindowsSandbox();

                    _windowsSandboxExeProcess.Start();

                    PInvoke.SetForegroundWindow((HWND)_windowsSandboxExeProcess.MainWindowHandle);
                }

                BringWindowsSandboxClientToForeground();

                return new ComputeSystemOperationResult();
            }
            catch (Exception ex)
            {
                State = ComputeSystemState.Unknown;

                _log.Error(ex, "Failed to start Windows Sandbox");
                TraceLogging.ExceptionThrown(ex);

                return new ComputeSystemOperationResult(
                    ex,
                    Resources.GetResource("WindowsSandboxFailedToStart", _log),
                    "Failed to start Windows Sandbox");
            }
        }).AsAsyncOperation();
    }

    private void WindowsSandboxProcessExited(object? sender, EventArgs e)
    {
        State = ComputeSystemState.Stopped;
        _windowsSandboxExeProcess?.Dispose();
        _windowsSandboxExeProcess = null;
    }

    private Process? GetWindowsSandboxClientProcess()
    {
        return Process.GetProcessesByName("WindowsSandboxClient").FirstOrDefault();
    }

    private void BringWindowsSandboxClientToForeground()
    {
        var clientProcess = GetWindowsSandboxClientProcess();
        var windowHandle = clientProcess?.MainWindowHandle ?? IntPtr.Zero;

        PInvoke.SetForegroundWindow((HWND)windowHandle);
    }

    public IAsyncOperation<ComputeSystemOperationResult> TerminateAsync(string options)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_windowsSandboxExeProcess == null || _windowsSandboxExeProcess.HasExited)
                {
                    State = ComputeSystemState.Stopped;
                    return new ComputeSystemOperationResult();
                }

                GetWindowsSandboxClientProcess()?.Kill();
                _windowsSandboxExeProcess.Kill();

                return new ComputeSystemOperationResult();
            }
            catch (Exception ex)
            {
                State = ComputeSystemState.Unknown;

                _log.Error(ex, "Failed to terminate Windows Sandbox");
                TraceLogging.ExceptionThrown(ex);

                return new ComputeSystemOperationResult(
                    ex,
                    Resources.GetResource("FailedToTerminateWindowsSandbox", _log),
                    "Failed to terminate Windows Sandbox");
            }
        }).AsAsyncOperation();
    }

    private IAsyncOperation<ComputeSystemOperationResult> NotImplemntedComputeSystemOperation()
    {
        NotImplementedException ex = new("This operation is not implemented.");
        ComputeSystemOperationResult result = new(ex, Resources.GetResource("NotImplemented", _log), ex.Message);

        return Task.FromResult(result).AsAsyncOperation();
    }

    public IApplyConfigurationOperation? CreateApplyConfigurationOperation(string configuration)
    {
        return null;
    }

    public IAsyncOperation<ComputeSystemOperationResult> CreateSnapshotAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> DeleteAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> DeleteSnapshotAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> ModifyPropertiesAsync(string inputJson) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> PauseAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> RestartAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> ResumeAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> RevertSnapshotAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> SaveAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> ShutDownAsync(string options) => NotImplemntedComputeSystemOperation();

    public IAsyncOperation<ComputeSystemOperationResult> StartAsync(string options) => NotImplemntedComputeSystemOperation();

    public void Dispose()
    {
        _windowsSandboxExeProcess?.Dispose();
        GC.SuppressFinalize(this);
    }
}
