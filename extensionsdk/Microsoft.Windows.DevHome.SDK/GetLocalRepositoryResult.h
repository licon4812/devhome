#pragma once
#include "GetLocalRepositoryResult.g.h"

namespace winrt::Microsoft::Windows::DevHome::SDK::implementation
{
    struct GetLocalRepositoryResult : GetLocalRepositoryResultT<GetLocalRepositoryResult>
    {
        GetLocalRepositoryResult() = default;

        const explicit GetLocalRepositoryResult(winrt::Microsoft::Windows::DevHome::SDK::ILocalRepository const& repository);
        const GetLocalRepositoryResult(winrt::hresult const& e, hstring const& displayMessage, hstring const& diagnosticText);
        winrt::Microsoft::Windows::DevHome::SDK::ILocalRepository Repository();
        winrt::Microsoft::Windows::DevHome::SDK::ProviderOperationResult Result();

    private:
        winrt::Microsoft::Windows::DevHome::SDK::ILocalRepository _repository;
        winrt::Microsoft::Windows::DevHome::SDK::ProviderOperationResult _result;
    };
}
namespace winrt::Microsoft::Windows::DevHome::SDK::factory_implementation
{
    struct GetLocalRepositoryResult : GetLocalRepositoryResultT<GetLocalRepositoryResult, implementation::GetLocalRepositoryResult>
    {
    };
}
