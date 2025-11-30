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
        ~CoreCLRHost();

        bool Initialize(const std::wstring& runtimePath, const std::wstring& assemblyPath);
        bool Shutdown();
        bool ExecuteAssembly(const std::wstring& assemblyPath, int argc = 0, const char** argv = nullptr, unsigned int* exitCode = nullptr);

        template<typename TDelegate>
        bool CreateDelegate(const std::string& assemblyName, const std::string& typeName, const std::string& methodName, TDelegate **delegatePtr)
        {
            if (!m_hostHandle || !m_coreclr_create_delegate)
            {
                return false;
            }

            int result = m_coreclr_create_delegate(
                m_hostHandle,
                m_domainId,
                assemblyName.c_str(),
                typeName.c_str(),
                methodName.c_str(),
                reinterpret_cast<void **>(delegatePtr)
            );

            return result >= 0;
        }

        bool IsInitialized() const { return m_hostHandle != nullptr; }

    private:
        // Helper methods
        std::string WStringToString(const std::wstring& wstr);
        std::wstring GetTrustedPlatformAssemblies(const std::wstring& runtimePath);
        std::wstring GetDirectory(const std::wstring& path);
        void BuildTpaList(const std::wstring& directory, std::wstring& tpaList);

        // CoreCLR runtime handles
        HMODULE m_coreCLRModule;
        void* m_hostHandle;
        unsigned int m_domainId;

        // CoreCLR function pointers
        coreclr_initialize_ptr m_coreclr_initialize;
        coreclr_shutdown_ptr m_coreclr_shutdown;
        coreclr_create_delegate_ptr m_coreclr_create_delegate;
        coreclr_execute_assembly_ptr m_coreclr_execute_assembly;

        // Runtime information
        std::wstring m_runtimePath;
        std::wstring m_assemblyPath;
    };
}

#endif