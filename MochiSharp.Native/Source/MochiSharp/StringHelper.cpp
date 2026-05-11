#include "PCH.hpp"

#include "StringHelper.hpp"

namespace mochi
{
    UCString StringHelper::ConvertUtf8ToWide(std::string_view str)
    {
#ifdef MOCHI_WIDE_CHARS
        int length = MultiByteToWideChar(CP_UTF8, 0, str.data(), static_cast<int>(str.length()), nullptr, 0);
        auto result = UCString(length, UCChar(0));
        MultiByteToWideChar(CP_UTF8, 0, str.data(), static_cast<int>(str.length()), result.data(), length);
        return result;
#else
        return UCString(str);
#endif
    }

    std::string StringHelper::ConvertWideToUtf8(UCStringView str)
    {
#ifdef MOCHI_WIDE_CHARS
        int requiredLength = WideCharToMultiByte(CP_UTF8, 0, str.data(), static_cast<int>(str.length()), nullptr, 0, nullptr, nullptr);
        std::string result(requiredLength, 0);
        (void)WideCharToMultiByte(CP_UTF8, 0, str.data(), static_cast<int>(str.length()), result.data(), requiredLength, nullptr, nullptr);
        return result;
#else
        return std::string(str);
#endif
    }
}