// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#include <filesystem>
#include <span>

#include <wil/com.h>
#include <wil/resource.h>

#include "DevHome.QuietBackgroundProcesses.h"
#include "PerformanceRecorderEngine.h"

struct com_ptr_deleter
{
    template<typename T>
    void operator()(_Pre_opt_valid_ _Frees_ptr_opt_ T p) const
    {
        if (p)
        {
            p.reset();
        }
    }
};

template<typename T, typename ArrayDeleter = wil::process_heap_deleter>
using unique_comptr_array = wil::unique_any_array_ptr<typename wil::com_ptr_nothrow<T>, ArrayDeleter, com_ptr_deleter>;

template<typename T>
unique_comptr_array<T> make_unique_comptr_array(size_t numOfElements)
{
    auto list = unique_comptr_array<T>(reinterpret_cast<wil::com_ptr_nothrow<T>*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, numOfElements * sizeof(wil::com_ptr_nothrow<T>))), numOfElements);
    THROW_IF_NULL_ALLOC(list.get());
    return list;
}

// Create a performance recorder engine
wil::com_ptr<ABI::DevHome::QuietBackgroundProcesses::IPerformanceRecorderEngine> MakePerformanceRecorderEngine();

// Read/write the performance data to/from disk
void WritePerformanceDataToDisk(_In_ PCWSTR path, const std::span<ProcessPerformanceSummary>& data);
std::vector<ProcessPerformanceSummary> ReadPerformanceDataFromDisk(_In_ PCWSTR path);

// Upload the performance data to the telemetry service
void UploadPerformanceDataTelemetry(std::chrono::milliseconds samplingPeriod, const std::span<ProcessPerformanceSummary>& data);
