#include "NativeDiagnostics.hpp"
#include <util/platform.h>
#include <windows.h>
namespace jk {
namespace { thread_local bool composing = false; }
FrameTrace& DiagnosticsTrace() { static FrameTrace trace([] { return os_gettime_ns(); }); return trace; }
bool InMainComposition() { return composing; }
bool DiagnosticsEnabled() { static const bool enabled = [] { char value[8]{}; return GetEnvironmentVariableA("JKILLER_DISABLE_TRACE", value, 8) != 1 || value[0] != '1'; }(); return enabled; }
void BeginDiagnosticsComposition() { composing = DiagnosticsEnabled(); }
void EndDiagnosticsComposition() { composing = false; }
}
