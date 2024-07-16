﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace DevHome.PI.Helpers;

public class ExternalToolCollection
{
    public int Version { get; set; }

    public ObservableCollection<ExternalTool> ExternalTools { get; set; }

    public ExternalToolCollection()
    {
        Version = ExternalToolsHelper.ToolsCollectionVersion;
        ExternalTools = [];
    }

    public ExternalToolCollection(int version, ObservableCollection<ExternalTool> tools)
    {
        Version = version;
        ExternalTools = tools;
    }
}
