#pragma once
#include <atomic>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>

namespace jk {
struct ProcessToken { std::uint32_t pid; std::uint64_t creation_filetime; std::wstring image_path; };
struct IProcessLifetime {
    virtual ~IProcessLifetime() = default;
    virtual ProcessToken Identity() const = 0;
    virtual bool Alive() const = 0;
    virtual std::uintptr_t WaitHandle() const { return 0; }
};
struct IProcessProbe {
    virtual ~IProcessProbe() = default;
    virtual std::shared_ptr<IProcessLifetime> Open(std::uint32_t pid) = 0;
};
class ProcessBinding;
struct BindingResult { std::unique_ptr<ProcessBinding> binding; std::string error; };
class ProcessBinding {
public:
    static BindingResult Open(const ProcessToken& token);
    static BindingResult Open(const ProcessToken& token, IProcessProbe& probe);
    bool CanEmit() const;
    void Seal();
    const ProcessToken& Identity() const { return token_; }
    std::uintptr_t WaitHandle() const { return lifetime_->WaitHandle(); }
private:
    ProcessBinding(ProcessToken token, std::shared_ptr<IProcessLifetime> lifetime) : token_(std::move(token)), lifetime_(std::move(lifetime)) { }
    ProcessToken token_;
    std::shared_ptr<IProcessLifetime> lifetime_;
    mutable std::atomic_bool sealed_{false};
};
std::optional<ProcessToken> ReadProcessToken(std::uint32_t pid);
}
