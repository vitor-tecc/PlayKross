#include "ModuleState.hpp"
#include "LocalControlPipe.hpp"
#include <obs-websocket-api.h>
#include <windows.h>
#include <set>
#include <nlohmann/json.hpp>
#include "InputPolicy.hpp"

namespace jk {
namespace {
LocalControlPipe control;
std::mutex controller_mutex;
std::optional<ProcessToken> controller_identity;
std::unique_ptr<ProcessBinding> controller_lifetime;
bool controller_expected = false;
std::wstring Environment(const wchar_t* name) {
    const DWORD size = GetEnvironmentVariableW(name, nullptr, 0);
    if (!size || size > 32768) return {};
    std::wstring value(size, L'\0');
    GetEnvironmentVariableW(name, value.data(), size); value.pop_back(); return value;
}
std::string ProcessMessage(const std::string& text) {
    using Json = nlohmann::json;
    auto root = Json::parse(text, nullptr, false);
    if (!root.is_object()) return "{\"op\":7,\"d\":{\"requestId\":\"\",\"requestStatus\":{\"result\":false,\"code\":400,\"comment\":\"Malformed control message\"}}}";
    const auto op = root.value("op", -1);
    if (op == 0) {
        return Json{{"op",0},{"d",{{"rpcVersion",1},{"obsStudioVersion",obs_get_version_string()},{"transport","local-pipe"},{"ownerVerified",true}}}}.dump();
    }
    if (op == 1) return "{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}";
    auto request = root.value("d", Json::object());
    if (!request.is_object()) return "{\"op\":7,\"d\":{\"requestId\":\"\",\"requestStatus\":{\"result\":false,\"code\":400}}}";
    const std::string type = request.value("requestType", "");
    const std::string id = request.value("requestId", "");
    static const std::set<std::string> permitted{
        "GetVersion","GetStats","GetInputKindList","GetInputList","GetInputSettings","GetInputPropertiesListPropertyItems",
        "CreateInput","RemoveInput","SetInputSettings","GetInputAudioTracks","SetInputAudioTracks","GetInputAudioMonitorType","SetInputAudioMonitorType",
        "GetSceneList","CreateScene","GetCurrentProgramScene","SetCurrentProgramScene","GetSceneItemList","SetSceneItemTransform","GetSceneItemTransform",
        "GetVideoSettings","SetVideoSettings","GetStreamStatus","StartStream","StopStream","GetStreamServiceSettings","SetStreamServiceSettings",
        "GetProfileParameter","SetProfileParameter","GetCurrentProfile","GetProfileList","SetCurrentProfile","CallVendorRequest"
    };
    Json data{{"requestId",id}}, status{{"result",false},{"code",204},{"comment","Control request is not permitted."}};
    if (op == 6 && permitted.contains(type)) {
        auto parameters = request.value("requestData", Json::object());
        if (!parameters.is_object()) return Json{{"op",7},{"d",{{"requestId",id},{"requestStatus",{{"result",false},{"code",400},{"comment","Request data must be an object."}}}}}}.dump();
        if (type == "CreateInput") {
            const std::string kind = parameters.value("inputKind", "");
            if (!AllowsCreateInput(kind)) return Json{{"op",7},{"d",{{"requestId",id},{"requestStatus",{{"result",false},{"code",400},{"comment","Only approved video sources are permitted; application audio requires an enforced exclusion boundary."}}}}}}.dump();
            if (!parameters.contains("inputSettings")) parameters["inputSettings"] = Json::object();
            if (!parameters["inputSettings"].is_object()) return Json{{"op",7},{"d",{{"requestId",id},{"requestStatus",{{"result",false},{"code",400}}}}}}.dump();
            parameters["inputSettings"]["capture_audio"] = false;
        }
        if (type == "SetInputSettings") {
            auto* source = obs_get_source_by_name(parameters.value("inputName", "").c_str());
            if (source) {
                const std::string kind = obs_source_get_unversioned_id(source);
                obs_source_release(source);
                if (kind == "wasapi_input_capture" || kind == "wasapi_output_capture" || kind == "wasapi_process_output_capture")
                    return Json{{"op",7},{"d",{{"requestId",id},{"requestStatus",{{"result",false},{"code",400},{"comment","Unbound and device audio sources are prohibited."}}}}}}.dump();
            }
            if (!parameters.contains("inputSettings")) parameters["inputSettings"] = Json::object();
            if (!parameters["inputSettings"].is_object()) return Json{{"op",7},{"d",{{"requestId",id},{"requestStatus",{{"result",false},{"code",400}}}}}}.dump();
            parameters["inputSettings"]["capture_audio"] = false;
        }
        const auto arguments = parameters.dump();
        // The maintained C API accepts raw JSON. obs_data_t cannot represent primitive arrays.
        calldata_t call{};
        calldata_set_string(&call, "request_type", type.c_str());
        calldata_set_string(&call, "request_data", arguments.c_str());
        auto* handler = obs_websocket_get_ph();
        if (handler) proc_handler_call(handler, "call_request", &call);
        auto* result = static_cast<obs_websocket_request_response*>(calldata_ptr(&call, "response"));
        calldata_free(&call);
        if (result) {
            status = {{"result",result->status_code == 100},{"code",result->status_code},{"comment",result->comment ? result->comment : ""}};
            if (result->response_data) {
                auto payload = Json::parse(result->response_data, nullptr, false);
                if (!payload.is_discarded()) data["responseData"] = std::move(payload);
            }
            obs_websocket_request_response_free(result);
        } else {
            status = {{"result",false},{"code",207},{"comment","OBS request API is not ready."}};
        }
    }
    data["requestStatus"] = std::move(status);
    return Json{{"op",7},{"d",std::move(data)}}.dump();
}
}
void StartLocalControl() {
    const auto name = Environment(L"JKILLER_CONTROL_PIPE");
    if (name.empty()) return;
    try {
        const auto pid = std::stoul(Environment(L"JKILLER_OWNER_PID"));
        if (!pid || pid > UINT32_MAX) throw std::runtime_error("Invalid controller PID.");
        const auto created = std::stoull(Environment(L"JKILLER_OWNER_CREATION"));
        const ProcessToken owner{static_cast<std::uint32_t>(pid), created, Environment(L"JKILLER_OWNER_IMAGE")};
        auto lifetime = ProcessBinding::Open(owner);
        if (!lifetime.binding) throw std::runtime_error(lifetime.error);
        if (!control.Start(name, owner, ProcessMessage)) throw std::runtime_error("Private control pipe could not start.");
        { std::lock_guard lock(controller_mutex); controller_identity = owner; controller_lifetime = std::move(lifetime.binding); controller_expected = true; }
        blog(LOG_INFO, "[JKiller] local-only controller pipe ready; remote clients prohibited");
    } catch (const std::exception& error) {
        { std::lock_guard lock(controller_mutex); controller_expected = true; }
        blog(LOG_ERROR, "[JKiller] private control unavailable: %s", error.what());
    }
}
void StopLocalControl() { control.Stop(); std::lock_guard lock(controller_mutex); controller_identity.reset(); controller_lifetime.reset(); controller_expected = false; }
std::optional<ProcessToken> ControllerIdentity() { std::lock_guard lock(controller_mutex); return controller_identity; }
bool ControllerAlive() { std::lock_guard lock(controller_mutex); return controller_lifetime && controller_lifetime->CanEmit(); }
bool ControllerLost() { std::lock_guard lock(controller_mutex); return controller_expected && (!controller_lifetime || !controller_lifetime->CanEmit()); }
}
