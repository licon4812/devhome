﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Windows.DevHome.SDK;

namespace FileExplorerGitIntegration.Helpers;

public class GitCommandRunnerResultInfo
{
    public ProviderOperationStatus Status { get; set; }

    public string? Output { get; set; }

    public string? DisplayMessage { get; set; }

    public string? DiagnosticText { get; set; }

    public Exception? Ex { get; set; }

    public string? Arguments { get; set; }

    public GitCommandRunnerResultInfo(ProviderOperationStatus status, string? output)
    {
        Status = status;
        Output = output;
    }

    public GitCommandRunnerResultInfo(ProviderOperationStatus status, string? displayMessage, string? diagnosticText, Exception? ex, string? args)
    {
        Status = status;
        DisplayMessage = displayMessage;
        DiagnosticText = diagnosticText;
        Ex = ex;
        Arguments = args;
    }
}
