#pragma once
#ifndef MOCHI_STRING_HELPER_HPP
#define MOCHI_STRING_HELPER_HPP

#include "Core.hpp"

namespace mochi
{
    class StringHelper
    {
    public:
        static UCString ConvertUtf8ToWide(std::string_view str);
        static std::string ConvertWideToUtf8(UCStringView str);
    };
}

#endif