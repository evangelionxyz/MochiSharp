// Copyright (c) 2026 Evangelion Manuhutu

#pragma once
#ifndef MOCHI_PCH_HPP
#define MOCHI_PCH_HPP

#include <iostream>

#include <cstring>
#include <string>

#include <vector>
#include <array>
#include <unordered_map>
#include <unordered_set>
#include <map>
#include <optional>
#include <set>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <Windows.h>
#else
#include <dlfcn.h>
#endif

#include <filesystem>
#include <functional>
#include <utility>

#include <coreclr_delegates.h>
#include <hostfxr.h>

#endif
