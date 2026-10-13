#include "ModuleState.hpp"
#include "VideoLease.hpp"
#include <windows.h>
#include <map>
#include <memory>
#include <string>
#include <stdexcept>
#include "winrt-capture.h"

namespace jk {
namespace {
struct CaptureApi {
    decltype(&winrt_capture_supported) supported{};
    decltype(&winrt_capture_init_window) window{};
    decltype(&winrt_capture_init_monitor) monitor{};
    decltype(&winrt_capture_free) free{};
    decltype(&winrt_capture_active) active{};
    decltype(&winrt_capture_render) render{};
    decltype(&winrt_capture_width) width{};
    decltype(&winrt_capture_height) height{};
    CaptureApi() {
        wchar_t path[32768]{};
        if (!GetModuleFileNameW(nullptr, path, 32768)) throw std::runtime_error("Owned engine path unavailable.");
        std::wstring file(path); file.resize(file.find_last_of(L"\\/") + 1); file += L"libobs-winrt.dll";
        auto library = LoadLibraryExW(file.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!library) throw std::runtime_error("Pinned Windows GPU capture component unavailable.");
#define LOAD(member, symbol) member = reinterpret_cast<decltype(member)>(GetProcAddress(library, #symbol)); if (!member) throw std::runtime_error("Pinned GPU capture export unavailable.")
        LOAD(supported, winrt_capture_supported); LOAD(window, winrt_capture_init_window); LOAD(monitor, winrt_capture_init_monitor);
        LOAD(free, winrt_capture_free); LOAD(active, winrt_capture_active); LOAD(render, winrt_capture_render);
        LOAD(width, winrt_capture_width); LOAD(height, winrt_capture_height);
#undef LOAD
    }
};
CaptureApi& Api() { static CaptureApi api; return api; }
std::wstring Wide(const char* text) {
    const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, nullptr, 0);
    if (size <= 1) return {};
    std::wstring result(size, L'\0'); MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, result.data(), size); result.pop_back(); return result;
}
struct VideoSource {
    obs_source_t* source{};
    std::uint64_t handle{}, generation{};
    bool window = false;
    bool attempted = false;
    std::unique_ptr<ProcessBinding> binding;
    std::unique_ptr<VideoLease> lease;
    winrt_capture* capture{};
    std::string error;
    std::mutex mutex;
    bool Ready() {
        if (!lease || !capture) return false;
        bool valid = false; DWORD observed_pid = 0;
        if (window) valid = IsWindow(reinterpret_cast<HWND>(handle)) && GetWindowThreadProcessId(reinterpret_cast<HWND>(handle), &observed_pid);
        else { MONITORINFO info{sizeof(info)}; valid = GetMonitorInfoW(reinterpret_cast<HMONITOR>(handle), &info); }
        return lease->CanRender(ControllerAlive() && (!binding || binding->CanEmit()), Api().active(capture), valid, observed_pid);
    }
};
std::mutex sources_mutex;
std::map<obs_source_t*, VideoSource*> sources;
std::mutex guard_mutex;
std::string selected_video;
std::uint64_t selected_generation = 0;
const char* Name(void*) { return "JKiller original window/display GPU capture"; }
void* Create(obs_data_t* settings, obs_source_t* source) {
    auto context = std::make_unique<VideoSource>(); context->source = source;
    try {
        if (!ControllerIdentity()) throw std::runtime_error("No original native controller.");
        context->handle = std::stoull(obs_data_get_string(settings, "native_handle"));
        context->generation = static_cast<std::uint64_t>(obs_data_get_int(settings, "selection_generation"));
        const std::string kind = obs_data_get_string(settings, "capture_kind");
        if ((kind != "window" && kind != "monitor") || !context->handle || !context->generation) throw std::runtime_error("Invalid original capture selection.");
        context->window = kind == "window";
        std::uint32_t pid = 0;
        if (context->window) {
            const auto requested = obs_data_get_int(settings, "pid");
            if (requested <= 0 || requested > UINT32_MAX) throw std::runtime_error("Original window process unavailable.");
            pid = static_cast<std::uint32_t>(requested);
            auto opened = ProcessBinding::Open({pid, std::stoull(obs_data_get_string(settings, "creation_filetime")), Wide(obs_data_get_string(settings, "image_path"))});
            if (!opened.binding) throw std::runtime_error(opened.error);
            context->binding = std::move(opened.binding);
            DWORD observed{}; GetWindowThreadProcessId(reinterpret_cast<HWND>(context->handle), &observed);
            if (!IsWindow(reinterpret_cast<HWND>(context->handle)) || observed != pid) throw std::runtime_error("Original window no longer belongs to the selected process.");
        }
        context->lease = std::make_unique<VideoLease>(pid);
    } catch (const std::exception& error) { context->error = error.what(); if (context->lease) context->lease->Seal(); }
    { std::lock_guard lock(sources_mutex); sources.emplace(source, context.get()); }
    return context.release();
}
void Destroy(void* data) {
    auto* context = static_cast<VideoSource*>(data);
    { std::lock_guard lock(sources_mutex); sources.erase(context->source); }
    if (context->capture) { obs_enter_graphics(); Api().free(context->capture); obs_leave_graphics(); }
    delete context;
}
void Render(void* data, gs_effect_t*) {
    auto* context = static_cast<VideoSource*>(data); std::lock_guard lock(context->mutex);
    if (context->Ready()) Api().render(context->capture); // A lost item renders blank; it is never recreated.
}
void Tick(void* data, float) {
    auto* c = static_cast<VideoSource*>(data);
    obs_enter_graphics();
    {
        std::lock_guard lock(c->mutex);
        if (!c->attempted && c->lease && c->error.empty()) {
            c->attempted = true;
            try {
                DWORD observed = 0; MONITORINFO info{sizeof(info)};
                const bool valid = c->window ? (IsWindow(reinterpret_cast<HWND>(c->handle)) && GetWindowThreadProcessId(reinterpret_cast<HWND>(c->handle), &observed)) : GetMonitorInfoW(reinterpret_cast<HMONITOR>(c->handle), &info);
                if (!c->lease->CanRender(!c->binding || c->binding->CanEmit(), true, valid, observed)) throw std::runtime_error("Original capture selection was lost before activation.");
                auto& api = Api();
                if (!api.supported()) throw std::runtime_error("Windows Graphics Capture unavailable.");
                // Like the maintained stock source, initialize on OBS's graphics thread.
                // This thread pumps the dispatcher required by the non-free-threaded frame pool.
                c->capture = c->window ? api.window(FALSE, reinterpret_cast<HWND>(c->handle), TRUE, TRUE) : api.monitor(FALSE, reinterpret_cast<HMONITOR>(c->handle), TRUE);
                if (!c->capture) throw std::runtime_error("Original capture item could not initialize.");
            } catch (const std::exception& error) { c->error = error.what(); c->lease->Seal(); }
        }
    }
    obs_leave_graphics();
}
uint32_t Width(void* data) { auto* c = static_cast<VideoSource*>(data); obs_enter_graphics(); uint32_t value; { std::lock_guard lock(c->mutex); value = c->capture ? Api().width(c->capture) : 0; } obs_leave_graphics(); return value; }
uint32_t Height(void* data) { auto* c = static_cast<VideoSource*>(data); obs_enter_graphics(); uint32_t value; { std::lock_guard lock(c->mutex); value = c->capture ? Api().height(c->capture) : 0; } obs_leave_graphics(); return value; }
void Update(void* data, obs_data_t* settings) {
    auto* c = static_cast<VideoSource*>(data); std::lock_guard lock(c->mutex);
    bool changed = std::to_string(c->handle) != obs_data_get_string(settings, "native_handle") || c->generation != static_cast<std::uint64_t>(obs_data_get_int(settings, "selection_generation")) || c->window != (std::string(obs_data_get_string(settings, "capture_kind")) == "window");
    if (c->binding) { const auto& original = c->binding->Identity(); changed |= original.pid != obs_data_get_int(settings, "pid") || std::to_string(original.creation_filetime) != obs_data_get_string(settings, "creation_filetime") || original.image_path != Wide(obs_data_get_string(settings, "image_path")); }
    if (changed && c->lease) c->lease->Seal();
}
}
void RegisterVideoSource() {
    obs_source_info info{}; info.id = "jk_bound_capture"; info.type = OBS_SOURCE_TYPE_INPUT;
    info.output_flags = OBS_SOURCE_VIDEO | OBS_SOURCE_CUSTOM_DRAW | OBS_SOURCE_SRGB;
    info.get_name = Name; info.create = Create; info.destroy = Destroy; info.update = Update;
    info.video_render = Render; info.get_width = Width; info.get_height = Height;
    info.video_tick = Tick;
    obs_register_source(&info);
}
bool VideoSourceReady(const char* name, std::uint64_t generation) {
    if (!name || !*name) return true;
    obs_enter_graphics(); bool ready = false;
    {
        std::lock_guard lock(sources_mutex);
        for (const auto& [source, c] : sources) if (std::string(obs_source_get_name(source)) == name) {
            std::lock_guard capture_lock(c->mutex); ready = c->generation == generation && c->Ready() && Api().width(c->capture) > 0 && Api().height(c->capture) > 0;
        }
    }
    obs_leave_graphics(); return ready;
}
void SelectGuardVideo(std::string name, std::uint64_t generation) { std::lock_guard lock(guard_mutex); selected_video = std::move(name); selected_generation = generation; }
bool GuardVideoReady() { std::string name; std::uint64_t generation; { std::lock_guard lock(guard_mutex); name = selected_video; generation = selected_generation; } return VideoSourceReady(name.c_str(), generation); }
void DescribeVideo(obs_data_t* response) {
    obs_enter_graphics();
    {
    std::lock_guard lock(sources_mutex);
    obs_data_set_bool(response, "videoReady", false);
    for (const auto& [source, c] : sources) if (std::string(obs_source_get_name(source)) == "JK Video") {
        std::lock_guard capture_lock(c->mutex);
        obs_data_set_bool(response, "videoReady", c->Ready() && Api().width(c->capture) > 0 && Api().height(c->capture) > 0);
        obs_data_set_int(response, "captureWidth", c->capture ? Api().width(c->capture) : 0); obs_data_set_int(response, "captureHeight", c->capture ? Api().height(c->capture) : 0);
        obs_data_set_string(response, "captureApi", "Windows Graphics Capture / D3D11 texture"); obs_data_set_string(response, "videoError", c->error.c_str()); break;
    }
    }
    obs_leave_graphics();
}
}
