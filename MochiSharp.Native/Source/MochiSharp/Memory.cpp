#include "PCH.hpp"

#include "Memory.hpp"

#ifdef MOCHI_PLATFORM_WINDOWS
#   include <combaseapi.h>
#endif

namespace mochi
{
    void *Memory::AllocHGlobal(size_t sz)
    {
#ifdef MOCHI_PLATFORM_WINDOWS
        return LocalAlloc(LMEM_FIXED | LMEM_ZEROINIT, sz);
#else
        return malloc(sz);
#endif
    }

    void Memory::FreeHGlobal(void *ptr)
    {
#ifdef MOCHI_PLATFORM_WINDOWS
        LocalFree(ptr);
#else
        free(ptr);
#endif
    }

    UCChar *Memory::StringToCoTaskMemAuto(UCStringView str)
    {
        size_t length = str.length() + 1;
        size_t size = length * sizeof(UCChar);

#ifdef MOCHI_PLATFORM_WINDOWS
        auto buffer = static_cast<UCChar *>(CoTaskMemAlloc(size));

        if (buffer != nullptr)
        {
            memset(buffer, 0xCE, size);
            wcscpy(buffer, str.data());
        }
#else
        UCChar *buffer;
        if ((buffer = static_cast<UCChar *>(calloc(length, sizeof(UCChar)))))
        {
            strcpy(buffer, str.data());
        }
#endif
        return buffer;
    }

    void Memory::FreeCoTaskMem(void *mem)
    {
#ifdef MOCHI_PLATFORM_WINDOWS
        CoTaskMemFree(mem);
#else
        FreeHGlobal(mem);
#endif
    }
}