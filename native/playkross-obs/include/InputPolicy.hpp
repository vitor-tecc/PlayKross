#pragma once
#include <string_view>
namespace jk {
bool AllowsCreateInput(std::string_view kind);
bool AllowsApplicationAudioImage(std::wstring_view image);
}
