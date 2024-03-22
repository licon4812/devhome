﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHome.Common.Extensions;
using DevHome.Common.Services;
using Microsoft.UI.Xaml;
using Windows.System;

namespace DevHome.Customization.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private INavigationService NavigationService { get; }

    public MainPageViewModel(
        INavigationService navigationService)
    {
        NavigationService = navigationService;
    }

    [RelayCommand]
    private async Task LaunchWindowsDeveloperSettings()
    {
        await Launcher.LaunchUriAsync(new("ms-settings:developers"));
    }

    [RelayCommand]
    private void NavigateToDeveloperFileExplorerPage()
    {
        NavigationService.NavigateTo(typeof(DeveloperFileExplorerViewModel).FullName!);
    }
}
