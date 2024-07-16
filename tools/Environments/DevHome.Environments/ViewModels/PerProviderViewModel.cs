﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Collections;
using DevHome.Common.Services;
using Microsoft.UI.Xaml;

namespace DevHome.Environments.ViewModels;

// View model representing a compute system provider and its associated compute systems.
public partial class PerProviderViewModel : ObservableObject
{
    public string ProviderName { get; }

    public string ProviderID { get; }

    public string DecoratedDevID { get; }

    public ObservableCollection<ComputeSystemCardBase> ComputeSystems { get; }

    public AdvancedCollectionView ComputeSystemAdvancedView { get; set; }

    private readonly StringResource _stringResource;

    private readonly Window _mainWindow;

    [ObservableProperty]
    private bool _isVisible = true;

    public PerProviderViewModel(string name, string id, string associatedDevID, List<ComputeSystemCardBase> computeSystems, Window mainWindow)
    {
        ProviderName = name;
        ProviderID = id;
        DecoratedDevID = associatedDevID.Length > 0 ? '(' + associatedDevID + ')' : string.Empty;
        ComputeSystems = new ObservableCollection<ComputeSystemCardBase>(computeSystems);
        _mainWindow = mainWindow;

        _stringResource = new StringResource("DevHome.Environments.pri", "DevHome.Environments/Resources");
        ComputeSystemAdvancedView = new AdvancedCollectionView(ComputeSystems);
        ComputeSystemAdvancedView.SortDescriptions.Add(new SortDescription("IsCardCreating", SortDirection.Descending));
    }

    /// <summary>
    /// Updates the view model to show only the compute systems that match the search criteria.
    /// </summary>
    public void SearchHandler(string query)
    {
        ComputeSystemAdvancedView.Filter = system =>
        {
            if (system is CreateComputeSystemOperationViewModel createComputeSystemOperationViewModel)
            {
                return createComputeSystemOperationViewModel.EnvironmentName.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            if (system is ComputeSystemViewModel computeSystemViewModel)
            {
                var systemName = computeSystemViewModel.ComputeSystem!.DisplayName.Value;
                var systemAltName = computeSystemViewModel.ComputeSystem.SupplementalDisplayName.Value;
                return systemName.Contains(query, StringComparison.OrdinalIgnoreCase) || systemAltName.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        };

        _mainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            IsVisible = ComputeSystemAdvancedView.Count > 0;
        });
    }

    /// <summary>
    /// Updates the view model to sort the compute systems according to the sort criteria.
    /// </summary>
    /// <remarks>
    /// New SortDescription property names should be added as new properties to <see cref="ComputeSystemCardBase"/>
    /// </remarks>
    public void SortHandler(int selectedSortIndex)
    {
        ComputeSystemAdvancedView.SortDescriptions.Clear();
        switch (selectedSortIndex)
        {
            case 0:
                ComputeSystemAdvancedView.SortDescriptions.Add(new SortDescription("Name", SortDirection.Ascending));
                break;
            case 1:
                ComputeSystemAdvancedView.SortDescriptions.Add(new SortDescription("Name", SortDirection.Descending));
                break;
            case 2:
                ComputeSystemAdvancedView.SortDescriptions.Add(new SortDescription("LastConnected", SortDirection.Ascending));
                break;
        }
    }

    /// <summary>
    /// Updates the view model to filter the compute systems according to the provider.
    /// </summary>
    public void ProviderHandler(string currentProvider)
    {
        ComputeSystemAdvancedView.Filter = system =>
        {
            if (currentProvider.Equals(_stringResource.GetLocalized("AllProviders"), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (system is CreateComputeSystemOperationViewModel createComputeSystemOperationViewModel)
            {
                return createComputeSystemOperationViewModel.ProviderDisplayName.Equals(currentProvider, StringComparison.OrdinalIgnoreCase);
            }

            if (system is ComputeSystemViewModel computeSystemViewModel)
            {
                return computeSystemViewModel.ProviderDisplayName.Equals(currentProvider, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        };

        _mainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            IsVisible = ComputeSystemAdvancedView.Count > 0;
        });
    }

    public override string ToString()
    {
        StringBuilder description = new(ProviderName);

        if (!string.IsNullOrEmpty(DecoratedDevID))
        {
            description.Append(DecoratedDevID);
        }

        return description.ToString();
    }
}
