// Copyright (c) 2025 Evangelion Manuhutu

#ifndef CORECLR_HOST_H
#define CORECLR_HOST_H

#include "pch.h"
#include <string>
#include <vector>

// CoreCLR hosting types
typedef int (*coreclr_initialize_ptr)(const char* exePath, const char* appDomainFriendlyName, int propertyCount, const char** propertyKeys, const char** propertyValues, void** hostHandle, unsigned int* domainId);
typedef int (*coreclr_shutdown_ptr)(void* hostHandle, unsigned int domainId);
typedef int (*coreclr_create_delegate_ptr)(void* hostHandle, unsigned int domainId, const char* entryPointAssemblyName, const char* entryPointTypeName, const char* entryPointMethodName, void** delegate);
typedef int (*coreclr_execute_assembly_ptr)(void* hostHandle, unsigned int domainId, int argc, const char** argv, const char* managedAssemblyPath, unsigned int* exitCode);

namespace criollo
{
    class CoreCLRHost
    {
    public:
        CoreCLRHost();

        bool Initialize(const std::string& runtimePath, const std::string& assemblyPath);
        bool Shutdown();

        bool ExecuteAssembly(const std::string& assemblyPath, int argc = 0, const char** argv = nullptr, unsigned int* exitCode = nullptr);

        template<typename TDelegate>
        bool CreateDelegate(const std::string& assemblyName, const std::string& typeName, const std::string& methodName, TDelegate **delegatePtr)
        {
            if (!m_HostHandle || !m_coreclr_create_delegate)
            {
                return false;
            }

            int result = m_coreclr_create_delegate(
                m_HostHandle,
                m_DomainId,
                assemblyName.c_str(),
                typeName.c_str(),
                methodName.c_str(),
                reinterpret_cast<void **>(delegatePtr)
            );

            return result >= 0;
        }

        bool IsInitialized() const { return m_HostHandle != nullptr; }

    private:
        std::string GetTrustedPlatformAssemblies(const std::string& runtimePath) const;
        static std::string GetDirectory(const std::string& path);
        static void BuildTpaList(const std::string &directory, std::string &tpaList);

        // CoreCLR runtime handles
        HMODULE m_CoreCLRModule;
        void* m_HostHandle;
        unsigned int m_DomainId;

        // CoreCLR function pointers
        coreclr_initialize_ptr m_coreclr_initialize;
        coreclr_shutdown_ptr m_coreclr_shutdown;
        coreclr_create_delegate_ptr m_coreclr_create_delegate;
        coreclr_execute_assembly_ptr m_coreclr_execute_assembly;

        // Runtime information
        std::string m_RuntimePath;
        std::string m_AssemblyPath;
    };
}

#endif