#pragma once
#include "ProcessLoopback.hpp"
#include <string_view>
namespace jk {
struct EndpointRead {
    enum class Status { Data, Timeout, Stopped, Failed };
    Status status;
    PcmBlock block;
    std::string error;
};
struct IEndpointCaptureClient {
    virtual ~IEndpointCaptureClient() = default;
    virtual CaptureResult Open(std::wstring_view endpointId) = 0;
    virtual EndpointRead Read(uintptr_t stopEvent, uintptr_t controllerEvent) = 0;
    virtual void Stop() = 0;
};
std::unique_ptr<IEndpointCaptureClient> MakeEndpointCaptureClient();
}
