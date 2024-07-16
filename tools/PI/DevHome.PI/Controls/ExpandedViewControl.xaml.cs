// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using DevHome.PI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DevHome.PI.Controls;

public sealed partial class ExpandedViewControl : UserControl
{
    private readonly ExpandedViewControlViewModel viewModel = new();

    public ExpandedViewControl()
    {
        InitializeComponent();
        viewModel.NavigationService.Frame = PageFrame;
    }

    public Frame GetPageFrame()
    {
        return PageFrame;
    }

    public void NavigateTo(Type viewModelType)
    {
        viewModel.NavigateTo(viewModelType);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSettings(typeof(SettingsPageViewModel).FullName!);
    }

    public void NavigateToSettings(string viewModelType)
    {
        viewModel.NavigateToSettings(viewModelType);
    }

    private void GridSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
    }
}
