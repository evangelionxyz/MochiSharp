#pragma once
#ifndef MOCHI_HOST_INSTANCE_HPP
#define MOCHI_HOST_INSTANCE_HPP

#include "Core.hpp"
#include "MessageLevel.hpp"
#include "Assembly.hpp"
#include "ManagedObject.hpp"

#include <filesystem>
#include <functional>

namespace mochi
{
    using ExceptionCallbackFn = std::function<void(std::string_view)>;

    struct HostSettings
    {
        /// <summary>
        /// The file path to MochiSharp.runtimeconfig.json (e.g C:\Dev\MyProject\ThirdParty\MochiSharp)
        /// </summary>
        std::string MochiSharpDirectory;

        MessageCallbackFn MessageCallback = nullptr;
        MessageLevel MessageFilter = MessageLevel::All;

        ExceptionCallbackFn ExceptionCallback = nullptr;
    };

    enum class MochiSharpInitStatus
    {
        Success,
        MochiSharpManagedNotFound,
        MochiSharpManagedInitError,
        DotNetNotFound,
    };

    class HostInstance
    {
    public:
        MochiSharpInitStatus Initialize(HostSettings InSettings);
        void Shutdown();

        AssemblyLoadContext CreateAssemblyLoadContext(std::string_view InName);
        void UnloadAssemblyLoadContext(AssemblyLoadContext &InLoadContext);

        // `InDllPath` is a colon-separated list of paths from which AssemblyLoader will try and resolve load paths at runtime.
        // This does not affect the behavior of LoadAssembly from native code.
        AssemblyLoadContext CreateAssemblyLoadContext(std::string_view InName, std::string_view InDllPath);

    private:
        bool LoadHostFXR() const;
        bool InitializeMochiSharpManaged();
        void LoadMochiSharpFunctions();

        void *LoadMochiSharpManagedFunctionPtr(const std::filesystem::path &InAssemblyPath, const UCChar *InTypeName,
            const UCChar *InMethodName, const UCChar *InDelegateType = UnmanagedCallersOnly) const;

        template<typename TFunc>
        TFunc LoadMochiSharpManagedFunctionPtr(const UCChar *InTypeName, const UCChar *InMethodName, const UCChar *InDelegateType = UnmanagedCallersOnly) const
        {
            return (TFunc)LoadMochiSharpManagedFunctionPtr(m_MochiSharpManagedAssemblyPath, InTypeName, InMethodName, InDelegateType);
        }

    private:
        HostSettings m_Settings;
        std::filesystem::path m_MochiSharpManagedAssemblyPath;
        void *m_HostFXRContext = nullptr;
        bool m_Initialized = false;

        friend class AssemblyLoadContext;
    };
}

#endif