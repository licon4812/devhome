﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Serilog;

namespace CoreWidgetProvider.Helpers;

internal sealed class NetworkStats : IDisposable
{
    private readonly Dictionary<string, List<PerformanceCounter>> networkCounters = new();

    private Dictionary<string, Data> NetworkUsages { get; set; } = new Dictionary<string, Data>();

    private Dictionary<string, List<float>> NetChartValues { get; set; } = new Dictionary<string, List<float>>();

    public sealed class Data
    {
        public float Usage
        {
            get; set;
        }

        public float Sent
        {
            get; set;
        }

        public float Received
        {
            get; set;
        }
    }

    public NetworkStats()
    {
        InitNetworkPerfCounters();
    }

    private void InitNetworkPerfCounters()
    {
        PerformanceCounterCategory pcc = new PerformanceCounterCategory("Network Interface");
        var instanceNames = pcc.GetInstanceNames();
        foreach (var instanceName in instanceNames)
        {
            List<PerformanceCounter> instanceCounters = new List<PerformanceCounter>();
            instanceCounters.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", instanceName));
            instanceCounters.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", instanceName));
            instanceCounters.Add(new PerformanceCounter("Network Interface", "Current Bandwidth", instanceName));
            networkCounters.Add(instanceName, instanceCounters);
            NetChartValues.Add(instanceName, new List<float>());
            NetworkUsages.Add(instanceName, new Data());
        }
    }

    public void GetData()
    {
        float maxUsage = 0;
        foreach (var networkCounterWithName in networkCounters)
        {
            try
            {
                var sent = networkCounterWithName.Value[0].NextValue();
                var received = networkCounterWithName.Value[1].NextValue();
                var bandWidth = networkCounterWithName.Value[2].NextValue();
                if (bandWidth == 0)
                {
                    continue;
                }

                var usage = 8 * (sent + received) / bandWidth;
                var name = networkCounterWithName.Key;
                NetworkUsages[name].Sent = sent;
                NetworkUsages[name].Received = received;
                NetworkUsages[name].Usage = usage;

                List<float> chartValues = NetChartValues[name];
                lock (chartValues)
                {
                    ChartHelper.AddNextChartValue(usage * 100, chartValues);
                }

                if (usage > maxUsage)
                {
                    maxUsage = usage;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error getting network data.", ex);
            }
        }
    }

    public string CreateNetImageUrl(int netChartIndex)
    {
        return ChartHelper.CreateImageUrl(NetChartValues.ElementAt(netChartIndex).Value, ChartHelper.ChartType.Net);
    }

    public string GetNetworkName(int networkIndex)
    {
        if (NetChartValues.Count <= networkIndex)
        {
            return string.Empty;
        }

        return NetChartValues.ElementAt(networkIndex).Key;
    }

    public Data GetNetworkUsage(int networkIndex)
    {
        if (NetChartValues.Count <= networkIndex)
        {
            return new Data();
        }

        var currNetworkName = NetChartValues.ElementAt(networkIndex).Key;
        if (!NetworkUsages.TryGetValue(currNetworkName, out var value))
        {
            return new Data();
        }

        return value;
    }

    public int GetPrevNetworkIndex(int networkIndex)
    {
        if (NetChartValues.Count == 0)
        {
            return 0;
        }

        if (networkIndex == 0)
        {
            return NetChartValues.Count - 1;
        }

        return networkIndex - 1;
    }

    public int GetNextNetworkIndex(int networkIndex)
    {
        if (NetChartValues.Count == 0)
        {
            return 0;
        }

        if (networkIndex == NetChartValues.Count - 1)
        {
            return 0;
        }

        return networkIndex + 1;
    }

    public void Dispose()
    {
        foreach (var counterPair in networkCounters)
        {
            foreach (var counter in counterPair.Value)
            {
                counter.Dispose();
            }
        }
    }
}
