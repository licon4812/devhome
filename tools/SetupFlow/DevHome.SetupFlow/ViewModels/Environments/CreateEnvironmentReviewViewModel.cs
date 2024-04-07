﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.ComponentModel;
using DevHome.SetupFlow.Services;

namespace DevHome.SetupFlow.ViewModels.Environments;

public partial class CreateEnvironmentReviewViewModel : ReviewTabViewModelBase
{
    private readonly ISetupFlowStringResource _stringResource;

    public override bool HasItems => true;

    public CreateEnvironmentReviewViewModel(
        ISetupFlowStringResource stringResource)
    {
        _stringResource = stringResource;
        TabTitle = stringResource.GetLocalized(StringResourceKey.EnvironmentCreationReviewPageTitle);
    }
}
