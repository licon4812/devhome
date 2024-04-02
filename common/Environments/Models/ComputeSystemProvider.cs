﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using DevHome.Common.Environments.Helpers;
using DevHome.Common.Helpers;
using Microsoft.Windows.DevHome.SDK;
using Serilog;

namespace DevHome.Common.Environments.Models;

/// <summary>
/// Wrapper class for the IComputeSystemProvider interface that can be used throughout the application.
/// Note: Additional methods added to this class should be wrapped in try/catch blocks to ensure that
/// exceptions don't bubble up to the caller as the methods are cross proc COM calls.
/// </summary>
public class ComputeSystemProvider
{
    private readonly ILogger _log = Log.ForContext("SourceContext", nameof(ComputeSystemProvider));

    private readonly string errorString;

    private readonly IComputeSystemProvider _computeSystemProvider;

    public string Id { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public ComputeSystemProviderOperations SupportedOperations { get; private set; }

    public Uri Icon { get; }

    public ComputeSystemProvider(IComputeSystemProvider computeSystemProvider)
    {
        _computeSystemProvider = computeSystemProvider;
        Id = computeSystemProvider.Id;
        DisplayName = computeSystemProvider.DisplayName;
        SupportedOperations = computeSystemProvider.SupportedOperations;
        Icon = computeSystemProvider.Icon;
        errorString = StringResourceHelper.GetResource("ComputeSystemUnexpectedError", DisplayName);
    }

    public ComputeSystemAdaptiveCardResult CreateAdaptiveCardSessionForDeveloperId(IDeveloperId developerId, ComputeSystemAdaptiveCardKind sessionKind)
    {
        try
        {
            return _computeSystemProvider.CreateAdaptiveCardSessionForDeveloperId(developerId, sessionKind);
        }
        catch (Exception ex)
        {
            _log.Error($"CreateAdaptiveCardSessionWithDeveloperId for: {this} failed due to exception", ex);
            return new ComputeSystemAdaptiveCardResult(ex, errorString, ex.Message);
        }
    }

    public ComputeSystemAdaptiveCardResult CreateAdaptiveCardSession(IComputeSystem computeSystem, ComputeSystemAdaptiveCardKind sessionKind)
    {
        try
        {
            return _computeSystemProvider.CreateAdaptiveCardSessionForComputeSystem(computeSystem, sessionKind);
        }
        catch (Exception ex)
        {
            _log.Error($"CreateAdaptiveCardSessionWithComputeSystem for: {this} failed due to exception", ex);
            return new ComputeSystemAdaptiveCardResult(ex, errorString, ex.Message);
        }
    }

    public async Task<ComputeSystemsResult> GetComputeSystemsAsync(IDeveloperId developerId)
    {
        try
        {
            return await _computeSystemProvider.GetComputeSystemsAsync(developerId);
        }
        catch (Exception ex)
        {
            _log.Error($"GetComputeSystemsAsync for: {this} failed due to exception", ex);
            return new ComputeSystemsResult(ex, errorString, ex.Message);
        }
    }

    public override string ToString()
    {
        StringBuilder builder = new();
        builder.AppendLine(CultureInfo.InvariantCulture, $"ComputeSystem provider ID: {Id} ");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ComputeSystem provider display name: {DisplayName} ");

        var supportedOperations = EnumHelper.SupportedOperationsToString<ComputeSystemProviderOperations>(SupportedOperations);
        builder.AppendLine(CultureInfo.InvariantCulture, $"ComputeSystem provider supported operations : {string.Join(", ", supportedOperations)} ");
        return builder.ToString();
    }
}
