#pragma once
#ifndef MOCHI_MEMORY_HPP
#define MOCHI_MEMORY_HPP

#include "Core.hpp"

namespace mochi
{
    struct Memory
    {
        static void *AllocHGlobal(size_t sz);
        static void FreeHGlobal(void *ptr);

        static UCChar *StringToCoTaskMemAuto(UCStringView str);
        static void FreeCoTaskMem(void *mem);
    };
}

#endif