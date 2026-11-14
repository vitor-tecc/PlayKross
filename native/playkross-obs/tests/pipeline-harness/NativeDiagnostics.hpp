#pragma once
#include "FrameTrace.hpp"
namespace jk {
inline FrameTrace& DiagnosticsTrace() { static FrameTrace trace([] { return 1ULL; }); return trace; }
inline bool DiagnosticsEnabled() { return false; }
inline void BeginDiagnosticsComposition() {}
inline void EndDiagnosticsComposition() {}
}
