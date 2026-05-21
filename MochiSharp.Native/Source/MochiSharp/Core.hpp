#pragma once
#ifndef CORE_HPP
#define CORE_HPP

#include <string_view>
#include <string>
#include <cstdint>
#include <cwchar>

#define MOCHI_DEPRECATE_MSG_P(s, x) s ". See `" x "`"

#define MOCHI_GLOBAL_ALC_MSG "Global type cache has been superseded by Assembly/ALC-local type APIs"
#define MOCHI_GLOBAL_ALC_MSG_P(x) MOCHI_DEPRECATE_MSG_P(MOCHI_GLOBAL_ALC_MSG, #x)

#define MOCHI_LEAK_UC_TYPES_MSG "Global namespace string type abstraction will be removed"
#define MOCHI_LEAK_UC_TYPES_MSG_P(x) MOCHI_DEPRECATE_MSG_P(MOCHI_LEAK_UC_TYPES_MSG, #x)

#ifdef _WIN32
#   define MOCHI_PLATFORM_WINDOWS
#   define PLATFORM_MAX_PATH MAX_PATH
#elif defined(__APPLE__)
#   define MOCHI_PLATFORM_APPLE
#elif defined(__linux__)
#   include <limits.h>
#   ifndef PATH_MAX
#       include<linux/limits.h>
#   endif
#   define PLATFORM_MAX_PATH PATH_MAX
#else
#   define PLATFORM_MAX_PATH 4096
#endif

#ifdef MOCHI_PLATFORM_WINDOWS
#   define MOCHI_CALLTYPE __cdecl
#   define MOCHI_HOSTFXR_NAME "hostfxr.dll"

#   ifdef _WCHAR_T_DEFINED
#       define MOCHI_WIDE_CHARS
#   endif

#else
#   define MOCHI_CALLTYPE

#   ifdef MOCHI_PLATFORM_APPLE
#       define MOCHI_HOSTFXR_NAME "libhostfxr.dylib"
#   else
#       define MOCHI_HOSTFXR_NAME "libhostfxr.so"
#   endif

#endif

#ifdef MOCHI_WIDE_CHARS
#   define MOCHI_STR(s) L##s

using CharType [[deprecated(MOCHI_LEAK_UC_TYPES_MSG_P(mochi::UChar))]] = wchar_t;
using StringView [[deprecated(MOCHI_LEAK_UC_TYPES_MSG_P(mochi::UCStringView))]] = std::wstring_view;

namespace mochi
{
    using UCChar = wchar_t;
    using UCStringView = std::wstring_view;
    using UCString = std::wstring;
}
#else
#   define MOCHI_STR(s) s

using CharType [[deprecated(MOCHI_LEAK_UC_TYPES_MSG_P(mochi::UChar))]] = char_t;
using StringView [[deprecated(MOCHI_LEAK_UC_TYPES_MSG_P(mochi::UCStringview))]] = std::string_view;

namespace mochi
{
    using UCChar = char;
    using UCStringView = std::string_view;
    using UCString = std::string;
}
#endif

#define MOCHI_UNMANAGED_CALLERS_ONLY ((const UCChar*) (-1ULL))

namespace mochi
{
    using Bool32 = uint32_t;
    static_assert(sizeof(Bool32) == 4);

    enum class TypeAccessibility
    {
        Public,
        Private,
        Protected,
        Internal,
        ProtectedPublic,
        PrivateProtected
    };

    using TypeId = int32_t;
    using ManagedHandle = int32_t;

    struct InternalCall
    {
        const UCChar *Name;
        void *NativeFunctionPtr;
    };
}

#endif