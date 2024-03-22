﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHome.Common.Models;
using DevHome.Common.Services;
using Microsoft.UI.Xaml.Controls;

namespace DevHome.Settings.ViewModels;

public partial class ExperimentalFeaturesViewModel : ObservableObject
{
    public ObservableCollection<Breadcrumb> Breadcrumbs { get; }

    public List<ExperimentalFeature> ExperimentalFeatures { get; } = new();

    public ExperimentalFeaturesViewModel(IExperimentationService experimentationService)
    {
        ExperimentalFeatures = experimentationService!.ExperimentalFeatures.Where(x => x.IsVisible).OrderBy(x => x.Id).ToList();

        var stringResource = new StringResource("DevHome.Settings/Resources");
        Breadcrumbs = new ObservableCollection<Breadcrumb>
        {
            new(stringResource.GetLocalized("Settings_Header"), typeof(SettingsViewModel).FullName!),
            new(stringResource.GetLocalized("Settings_ExperimentalFeatures_Header"), typeof(ExperimentalFeaturesViewModel).FullName!),
        };
    }

    [RelayCommand]
    public void BreadcrumbBarItemClicked(BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index < Breadcrumbs.Count - 1)
        {
            var crumb = (Breadcrumb)args.Item;
            crumb.NavigateTo();
        }
    }
}
