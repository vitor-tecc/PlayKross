#pragma once
#include "ProcessBinding.hpp"
#include <functional>
namespace jk {
class LocalControlPipe {
public:
    using Handler = std::function<std::string(const std::string&)>;
    LocalControlPipe();
    ~LocalControlPipe();
    bool Start(const std::wstring& pipe_name, const ProcessToken& owner, Handler handler);
    void Stop();
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
