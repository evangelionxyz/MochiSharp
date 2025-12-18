// Copyright (c) 2025 Evangelion Manuhutu

#include "pch.h"

#include "CoreCLRHost.h"
#include <filesystem>
#include <iostream>
#include <array>
#include <print>
#include <locale>

namespace criollo
{
    CoreCLRHost::CoreCLRHost()
        : m_CoreCLRModule(nullptr)
        , m_HostHandle(nullptr)
        , m_DomainId(0)
        , m_coreclr_initialize(nullptr)
        , m_coreclr_shutdown(nullptr)
        , m_coreclr_create_delegate(nullptr)
        , m_coreclr_execute_assembly(nullptr)
    {
    }

    bool CoreCLRHost::Initialize(const std::string& runtimePath, const std::string& assemblyPath)
    {
        if (m_HostHandle != nullptr)
        {
            return false; // Already initialized
        }

        m_RuntimePath = runtimePath;
        m_AssemblyPath = assemblyPath;

        // Construct the CoreCLR DLL path
        const std::string coreCLRDllPath = runtimePath + "\\coreclr.dll";

        // Load CoreCLR DLL
        m_CoreCLRModule = LoadLibraryExA(coreCLRDllPath.c_str(), nullptr, 0);
        if (!m_CoreCLRModule)
        {
        	std::println("Failed to load {}", coreCLRDllPath);
            return false;
        }

        // Get CoreCLR hosting functions
        m_coreclr_initialize = reinterpret_cast<coreclr_initialize_ptr>(GetProcAddress(m_CoreCLRModule, "coreclr_initialize"));
		m_coreclr_shutdown = reinterpret_cast<coreclr_shutdown_ptr>(GetProcAddress(m_CoreCLRModule, "coreclr_shutdown"));
        m_coreclr_create_delegate = reinterpret_cast<coreclr_create_delegate_ptr>(GetProcAddress(m_CoreCLRModule, "coreclr_create_delegate"));
        m_coreclr_execute_assembly = reinterpret_cast<coreclr_execute_assembly_ptr>(GetProcAddress(m_CoreCLRModule, "coreclr_execute_assembly"));

        if (!m_coreclr_initialize || !m_coreclr_shutdown || !m_coreclr_create_delegate || !m_coreclr_execute_assembly)
        {
            FreeLibrary(m_CoreCLRModule);
            m_CoreCLRModule = nullptr;
            return false;
        }

        // Build Trusted Platform Assemblies (TPA) list
        const std::string tpaList = GetTrustedPlatformAssemblies(runtimePath);

        // Get the current executable path (native host)
        std::array<char, MAX_PATH> moduleFilename{};
        GetModuleFileNameA(nullptr, moduleFilename.data(), static_cast<DWORD>(moduleFilename.size()));
        const std::string exePath(moduleFilename.data());
        const std::string appPath = GetDirectory(assemblyPath);

        // Diagnostics
        std::cout << "[CoreCLRHost] runtimePath=" << runtimePath << std::endl;
        std::cout << "[CoreCLRHost] assemblyPath=" << assemblyPath << std::endl;
        std::cout << "[CoreCLRHost] appPath=" << appPath << std::endl;
        std::cout << "[CoreCLRHost] TPA length=" << tpaList.size() << std::endl;

        // Define CoreCLR properties
        std::vector<const char*> propertyKeys = {
            "TRUSTED_PLATFORM_ASSEMBLIES",
            "APP_PATHS",
            "APP_NI_PATHS",
            "NATIVE_DLL_SEARCH_DIRECTORIES",
            "PLATFORM_RESOURCE_ROOTS"
        };

        const std::string nativeDllSearchDirs = runtimePath + ";" + appPath;

        const char* propertyValues[] = {
            tpaList.c_str(),
            appPath.c_str(),
            appPath.c_str(),
            nativeDllSearchDirs.c_str(),
            appPath.c_str()
        };

        // Initialize CoreCLR
        const int result = m_coreclr_initialize(exePath.c_str(), "CriolloHost",
            static_cast<int>(propertyKeys.size()), propertyKeys.data(),
            propertyValues, &m_HostHandle, &m_DomainId
        );

        if (result < 0)
        {
            std::cout << "[CoreCLRHost] coreclr_initialize failed with code " << result << std::endl;
            FreeLibrary(m_CoreCLRModule);
            m_CoreCLRModule = nullptr;
            m_HostHandle = nullptr;
            return false;
        }

        return true;
    }

    bool CoreCLRHost::Shutdown()
    {
    	std::println("Shutting down CoreCLR...");

        if (m_HostHandle && m_coreclr_shutdown)
        {
            m_coreclr_shutdown(m_HostHandle, m_DomainId);
            m_HostHandle = nullptr;
            m_DomainId = 0;
        }

        // Avoid unloading coreclr.dll explicitly; let process teardown handle it to prevent
        // potential access violations from late CRT/managed callbacks.
        m_CoreCLRModule = nullptr;

        m_coreclr_initialize = nullptr;
        m_coreclr_shutdown = nullptr;
        m_coreclr_create_delegate = nullptr;
        m_coreclr_execute_assembly = nullptr;

        return true;
    }

    bool CoreCLRHost::ExecuteAssembly(const std::string& assemblyPath, int argc, const char** argv, unsigned int* exitCode)
    {
        if (!m_HostHandle || !m_coreclr_execute_assembly)
        {
            return false;
        }
        unsigned int tempExitCode = 0;
        
        int result = m_coreclr_execute_assembly(
            m_HostHandle,
            m_DomainId,
            argc,
            argv,
            assemblyPath.c_str(),
            exitCode ? exitCode : &tempExitCode
        );

        return result >= 0;
    }

    std::string CoreCLRHost::GetTrustedPlatformAssemblies(const std::string& runtimePath) const
    {
        std::string tpaList;
        BuildTpaList(runtimePath, tpaList);
        
        // Also add the app directory if different from runtime
        std::string appDir = GetDirectory(m_AssemblyPath);
        if (appDir != runtimePath)
        {
            BuildTpaList(appDir, tpaList);
        }
        
        return tpaList;
    }

    std::string CoreCLRHost::GetDirectory(const std::string& path)
    {
        std::filesystem::path p(path);
        if (std::filesystem::is_directory(p))
        {
            return path;
        }
        return p.parent_path().string();
    }

    void CoreCLRHost::BuildTpaList(const std::string& directory, std::string& tpaList)
    {
        try
        {
            if (!std::filesystem::exists(directory) || !std::filesystem::is_directory(directory))
            {
                return;
            }

            for (const auto& entry : std::filesystem::directory_iterator(directory))
            {
                if (!entry.is_regular_file())
                    continue;

                const std::string extension = entry.path().extension().string();
                if (extension == ".dll")
                {
                    if (!tpaList.empty())
                    {
                        tpaList += ";";
                    }
                    tpaList += entry.path().string();
                }
            }
        }
        catch (...)
        {
            // Ignore filesystem errors
        }
    }
}
