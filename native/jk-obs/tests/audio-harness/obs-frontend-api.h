#pragma once
#include "ModuleState.hpp"
enum obs_frontend_event { OBS_FRONTEND_EVENT_FINISHED_LOADING };
inline bool obs_frontend_streaming_active() { return false; }
inline void obs_frontend_streaming_stop() {}
inline void* obs_frontend_get_main_window_handle() { return nullptr; }
inline void obs_frontend_add_event_callback(void (*)(obs_frontend_event, void*), void*) {}
inline void obs_frontend_remove_event_callback(void (*)(obs_frontend_event, void*), void*) {}
