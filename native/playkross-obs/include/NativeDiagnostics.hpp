#pragma once
#include "FrameTrace.hpp"
namespace jk {
FrameTrace& DiagnosticsTrace();
bool InMainComposition();
bool DiagnosticsEnabled();
void BeginDiagnosticsComposition();
void EndDiagnosticsComposition();
}
