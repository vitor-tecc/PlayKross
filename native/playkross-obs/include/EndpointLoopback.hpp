#pragma once
#include "EndpointCaptureClient.hpp"
#include <mutex>
namespace jk {
class EndpointLoopback {
public:
    explicit EndpointLoopback(std::unique_ptr<IEndpointCaptureClient> client = {});
    ~EndpointLoopback();
    CaptureResult Start(const std::wstring& endpointId, ProcessBinding& controller, AudioSink& sink);
    void Stop();
    bool Running() const;
    std::string Error() const;
    AudioQueueStats QueueStats() const;
private:
    struct Impl;
    mutable std::mutex mutex_;
    std::shared_ptr<Impl> impl_;
    std::shared_ptr<Impl> pending_;
    std::unique_ptr<IEndpointCaptureClient> client_;
};
}
