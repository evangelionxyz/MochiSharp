#pragma once
#ifndef MOCHI_DOTNET_SERVICES
#define MOCHI_DOTNET_SERVICES

#include <string>

namespace mochi
{
    class DotnetServices
    {
    public:
        static bool RunMSBuild(const std::string &InSolutionPath, bool InBuildDebug = true);

    };
}
#endif