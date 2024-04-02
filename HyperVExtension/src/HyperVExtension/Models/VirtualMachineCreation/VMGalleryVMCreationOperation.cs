﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HyperVExtension.Common;
using HyperVExtension.Exceptions;
using HyperVExtension.Helpers;
using HyperVExtension.Models.VMGalleryJsonToClasses;
using HyperVExtension.Providers;
using HyperVExtension.Services;
using Microsoft.Windows.DevHome.SDK;
using Serilog;
using Windows.Foundation;
using Windows.Storage;

namespace HyperVExtension.Models.VirtualMachineCreation;

public delegate VMGalleryVMCreationOperation VmGalleryCreationOperationFactory(VMGalleryCreationUserInput parameters);

/// <summary>
/// Class that represents the VM gallery VM creation operation.
/// </summary>
public sealed class VMGalleryVMCreationOperation : IVMGalleryVMCreationOperation
{
    private readonly ILogger _log = Log.ForContext("SourceContext", ComponentName);

    private const string ComponentName = nameof(VMGalleryVMCreationOperation);

    private readonly IArchiveProviderFactory _archiveProviderFactory;

    private readonly IHyperVManager _hyperVManager;

    private readonly IDownloaderService _downloaderService;

    private readonly string _tempFolderSaveLocation = Path.GetTempPath();

    private readonly IStringResource _stringResource;

    private readonly IVMGalleryService _vmGalleryService;

    private readonly VMGalleryCreationUserInput _userInputParameters;

    public CancellationTokenSource CancellationTokenSource { get; private set; } = new();

    private readonly object _lock = new();

    public bool IsOperationInProgress { get; private set; }

    public bool IsOperationCompleted { get; private set; }

    public CreateComputeSystemResult? ComputeSystemResult { get; private set; }

    public StorageFile? ArchivedFile { get; private set; }

    public VMGalleryImage Image { get; private set; } = new();

    public event TypedEventHandler<ICreateComputeSystemOperation, CreateComputeSystemActionRequiredEventArgs>? ActionRequired = (s, e) => { };

    public event TypedEventHandler<ICreateComputeSystemOperation, CreateComputeSystemProgressEventArgs>? Progress;

    public VMGalleryVMCreationOperation(
        IStringResource stringResource,
        IVMGalleryService vmGalleryService,
        IDownloaderService downloaderService,
        IArchiveProviderFactory archiveProviderFactory,
        IHyperVManager hyperVManager,
        VMGalleryCreationUserInput parameters)
    {
        _stringResource = stringResource;
        _vmGalleryService = vmGalleryService;
        _userInputParameters = parameters;
        _archiveProviderFactory = archiveProviderFactory;
        _hyperVManager = hyperVManager;
        _downloaderService = downloaderService;
    }

    /// <summary>
    /// Reports the progress of an operation.
    /// </summary>
    /// <param name="value">The archive extraction operation returned by the progress handler which extracts the archive file</param>
    public void Report(IOperationReport value)
    {
        var displayText = Image.Name;

        if (value.ReportKind == ReportKind.ArchiveExtraction)
        {
            displayText = $"{ArchivedFile!.Name} ({Image.Name})";
        }

        UpdateProgress(value, value.LocalizationKey, displayText);
    }

    /// <summary>
    /// Starts the VM gallery operation.
    /// </summary>
    /// <returns>A result that contains information on whether the operation succeeded or failed</returns>
    public IAsyncOperation<CreateComputeSystemResult?> StartAsync()
    {
        return Task.Run(async () =>
        {
            try
            {
                lock (_lock)
                {
                    if (IsOperationInProgress)
                    {
                        var exception = new OperationInProgressException(_stringResource);
                        return new CreateComputeSystemResult(exception, exception.Message, exception.Message);
                    }
                    else if (IsOperationCompleted)
                    {
                        return ComputeSystemResult;
                    }

                    IsOperationInProgress = true;
                }

                var imageList = await _vmGalleryService.GetGalleryImagesAsync();
                if (imageList.Images.Count == 0)
                {
                    throw new NoVMImagesAvailableException(_stringResource);
                }

                Image = imageList.Images[_userInputParameters.SelectedImageListIndex];

                await DownloadImageAsync();
                var virtualMachineHost = _hyperVManager.GetVirtualMachineHost();
                var absoluteFilePathForVhd = GetUniqueAbsoluteFilePath(virtualMachineHost.VirtualHardDiskPath);

                // extract the archive file to the destination file.
                var archiveProvider = _archiveProviderFactory.CreateArchiveProvider(ArchivedFile!.FileType);

                await archiveProvider.ExtractArchiveAsync(this, ArchivedFile!, absoluteFilePathForVhd, CancellationTokenSource.Token);
                var virtualMachineName = MakeFileNameValid(_userInputParameters.NewVirtualMachineName);

                // Use the Hyper-V manager to create the VM.
                UpdateProgress(_stringResource.GetLocalized("CreationInProgress", virtualMachineName));
                var creationParameters = new VirtualMachineCreationParameters(
                    _userInputParameters.NewVirtualMachineName,
                    GetVirtualMachineProcessorCount(),
                    absoluteFilePathForVhd,
                    Image.Config.SecureBoot,
                    Image.Config.EnhancedSessionTransportType);

                ComputeSystemResult = new CreateComputeSystemResult(_hyperVManager.CreateVirtualMachineFromGallery(creationParameters));
            }
            catch (Exception ex)
            {
                _log.Error("Operation to create compute system failed", ex);
                ComputeSystemResult = new CreateComputeSystemResult(ex, ex.Message, ex.Message);
            }

            IsOperationCompleted = true;
            IsOperationInProgress = false;

            return ComputeSystemResult;
        }).AsAsyncOperation();
    }

    private void UpdateProgress(IOperationReport report, string localizedKey, string fileName)
    {
        var bytesReceivedSoFar = BytesHelper.ConvertBytesToString(report.BytesReceived);
        var totalBytesToReceive = BytesHelper.ConvertBytesToString(report.TotalBytesToReceive);
        var progressPercentage = (uint)((report.BytesReceived / (double)report.TotalBytesToReceive) * 100D);
        var displayString = _stringResource.GetLocalized(localizedKey, fileName, $"{bytesReceivedSoFar}/{totalBytesToReceive}");
        Progress?.Invoke(this, new CreateComputeSystemProgressEventArgs(displayString, progressPercentage));
    }

    private void UpdateProgress(string localizedString, uint percentage = 0u)
    {
        Progress?.Invoke(this, new CreateComputeSystemProgressEventArgs(localizedString, percentage));
    }

    /// <summary>
    /// Downloads the disk image from the Hyper-V VM gallery.
    /// </summary>
    private async Task DownloadImageAsync()
    {
        var downloadUri = new Uri(Image.Disk.Uri);
        var archivedFileName = _vmGalleryService.GetDownloadedArchiveFileName(Image);
        var archivedFileAbsolutePath = Path.Combine(_tempFolderSaveLocation, archivedFileName);

        // If the file already exists and has the correct hash, we don't need to download it again.
        if (File.Exists(archivedFileAbsolutePath))
        {
            ArchivedFile = await StorageFile.GetFileFromPathAsync(archivedFileAbsolutePath);
            if (await _vmGalleryService.ValidateFileSha256Hash(ArchivedFile))
            {
                return;
            }

            // hash is not valid, so we'll delete/overwrite the file and download it again.
            _log.Information("File already exists but hash is not valid. Deleting file and downloading again.");
            await DeleteFileIfExists(ArchivedFile!);
        }

        await _downloaderService.StartDownloadAsync(this, downloadUri, archivedFileAbsolutePath, CancellationTokenSource.Token);

        // Create the file to save the downloaded archive image to.
        ArchivedFile = await StorageFile.GetFileFromPathAsync(archivedFileAbsolutePath);

        // Download was successful, we'll check the hash of the file, and if it's valid, we'll extract it.
        if (!await _vmGalleryService.ValidateFileSha256Hash(ArchivedFile))
        {
            await ArchivedFile.DeleteAsync();
            throw new DownloadOperationFailedException(_stringResource.GetLocalized("DownloadOperationFailedCheckingHash"));
        }
    }

    private async Task DeleteFileIfExists(StorageFile file)
    {
        try
        {
            await file.DeleteAsync();
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to delete file {file.Path}", ex);
        }
    }

    private string MakeFileNameValid(string originalName)
    {
        const string escapeCharacter = "_";
        return string.Join(escapeCharacter, originalName.Split(Path.GetInvalidFileNameChars()));
    }

    private string GetUniqueAbsoluteFilePath(string defaultVirtualDiskPath)
    {
        var extension = Path.GetExtension(Image.Disk.ArchiveRelativePath);
        var expectedExtractedFileLocation = Path.Combine(defaultVirtualDiskPath, $"{_userInputParameters.NewVirtualMachineName}{extension}");
        var appendedNumber = 1u;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(expectedExtractedFileLocation);

        // If the extracted virtual hard disk file doesn't exist, we'll extract it to the temp folder.
        // If it does exist we'll need to extract the archive file and append a number to the file
        // as it will be a new file within the temp directory.
        while (File.Exists(expectedExtractedFileLocation))
        {
            expectedExtractedFileLocation = Path.Combine(defaultVirtualDiskPath, $"{fileNameWithoutExtension} ({appendedNumber++}){extension}");
        }

        return expectedExtractedFileLocation;
    }

    private int GetVirtualMachineProcessorCount()
    {
        // We'll use half the number of processors for the processor count of the VM just like VM gallery in Windows.
        return Math.Max(1, Environment.ProcessorCount / 2);
    }
}
