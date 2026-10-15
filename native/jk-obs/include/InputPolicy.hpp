#pragma once
#include <string_view>
namespace jk {
bool AllowsCreateInput(std::string_view kind);
bool AllowsExperimentalAudioImage(std::wstring_view image);
}
