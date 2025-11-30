// Copyright (c) 2025 Evangelion Manuhutu

#include "pch.h"

#include "CoreCLRHost.h"
#include <filesystem>
#include <array>
#include <codecvt>
#include <iostream>
#include <locale>

namespace criollo
{
    CoreCLRHost::CoreCLRHost()
        : m_coreCLRModule(nullptr)
        , m_hostHandle(nullptr)
        , m_domainId(0)
        , m_coreclr_initialize(nullptr)
        , m_coreclr_shutdown(nullptr)
        , m_coreclr_create_delegate(nullptr)
        , m_coreclr_execute_assembly(nullptr)
    {
    }

    CoreCLRHost::~CoreCLRHost()
    {
        Shutdown();
    }

    bool CoreCLRHost::Initialize(const std::wstring& runtimePath, const std::wstring& assemblyPath)
    {
        if (m_hostHandle != nullptr)
        {
            return false; // Already initialized
        }

        m_runtimePath = runtimePath;
        m_assemblyPath = assemblyPath;

        // Construct the CoreCLR DLL path
        std::wstring coreCLRDllPath = runtimePath + L"\\coreclr.dll";

        // Load CoreCLR DLL
        m_coreCLRModule = LoadLibraryExW(coreCLRDllPath.c_str(), nullptr, 0);
        if (!m_coreCLRModule)
        {
            std::wcout << "Failed to load " << coreCLRDllPath << L'\n';
            return false;
        }

        // Get CoreCLR hosting functions
        m_coreclr_initialize = (coreclr_initialize_ptr)(GetProcAddress(m_coreCLRModule, "coreclr_initialize"));
		m_coreclr_shutdown = (coreclr_shutdown_ptr)(GetProcAddress(m_coreCLRModule, "coreclr_shutdown"));
        m_coreclr_create_delegate = (coreclr_create_delegate_ptr)(GetProcAddress(m_coreCLRModule, "coreclr_create_delegate"));
        m_coreclr_execute_assembly = (coreclr_execute_assembly_ptr)(GetProcAddress(m_coreCLRModule, "coreclr_execute_assembly"));

        if (!m_coreclr_initialize || !m_coreclr_shutdown || !m_coreclr_create_delegate || !m_coreclr_execute_assembly)
        {
            FreeLibrary(m_coreCLRModule);
            m_coreCLRModule = nullptr;
            return false;
        }

        // Build Trusted Platform Assemblies (TPA) list
        std::wstring tpaList = GetTrustedPlatformAssemblies(runtimePath);

        // Get the current executable path
        std::array<wchar_t, MAX_PATH> exePath;
        GetModuleFileNameW(nullptr, exePath.data(), MAX_PATH);

        // Get app paths
        std::wstring appPath = GetDirectory(assemblyPath);
        
        // Convert paths to narrow strings
        std::string exePathStr = WStringToString(exePath.data());
        std::string appPathStr = WStringToString(appPath);
        std::string tpaListStr = WStringToString(tpaList);
        std::string runtimePathStr = WStringToString(runtimePath);

        // Define CoreCLR properties
        std::vector<const char*> propertyKeys = {
            "TRUSTED_PLATFORM_ASSEMBLIES",
            "APP_PATHS",
            "APP_NI_PATHS",
            "NATIVE_DLL_SEARCH_DIRECTORIES",
            "PLATFORM_RESOURCE_ROOTS"
        };

        const char* propertyValues[] = {
            tpaListStr.c_str(),
            appPathStr.c_str(),
            appPathStr.c_str(),
            runtimePathStr.c_str(),
            appPathStr.c_str()
        };

        // Initialize CoreCLR
        int result = m_coreclr_initialize(exePathStr.c_str(), "CriolloHost",
            static_cast<int>(propertyKeys.size()), propertyKeys.data(),
            propertyValues, &m_hostHandle, &m_domainId
        );

        if (result < 0)
        {
            FreeLibrary(m_coreCLRModule);
            m_coreCLRModule = nullptr;
            m_hostHandle = nullptr;
            return false;
        }

        return true;
    }

    bool CoreCLRHost::Shutdown()
    {
        if (m_hostHandle && m_coreclr_shutdown)
        {
            m_coreclr_shutdown(m_hostHandle, m_domainId);
            m_hostHandle = nullptr;
            m_domainId = 0;
        }

        if (m_coreCLRModule)
        {
            FreeLibrary(m_coreCLRModule);
            m_coreCLRModule = nullptr;
        }

        m_coreclr_initialize = nullptr;
        m_coreclr_shutdown = nullptr;
        m_coreclr_create_delegate = nullptr;
        m_coreclr_execute_assembly = nullptr;

        return true;
    }

    bool CoreCLRHost::ExecuteAssembly(const std::wstring& assemblyPath, int argc, const char** argv, unsigned int* exitCode)
    {
        if (!m_hostHandle || !m_coreclr_execute_assembly)
        {
            return false;
        }

        std::string assemblyPathStr = WStringToString(assemblyPath);
        unsigned int tempExitCode = 0;
        
        int result = m_coreclr_execute_assembly(
            m_hostHandle,
            m_domainId,
            argc,
            argv,
            assemblyPathStr.c_str(),
            exitCode ? exitCode : &tempExitCode
        );

        return result >= 0;
    }

    std::string CoreCLRHost::WStringToString(const std::wstring& wstr)
    {
        if (wstr.empty())
            return {};

        int size = WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), static_cast<int>(wstr.length()), nullptr, 0, nullptr, nullptr);
        std::string result(size, 0);
        WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), static_cast<int>(wstr.length()), &result[0], size, nullptr, nullptr);
        return result;
    }

    std::wstring CoreCLRHost::GetTrustedPlatformAssemblies(const std::wstring& runtimePath)
    {
        std::wstring tpaList;
        BuildTpaList(runtimePath, tpaList);
        
        // Also add the app directory if different from runtime
        std::wstring appDir = GetDirectory(m_assemblyPath);
        if (appDir != runtimePath)
        {
            BuildTpaList(appDir, tpaList);
        }
        
        return tpaList;
    }

    std::wstring CoreCLRHost::GetDirectory(const std::wstring& path)
    {
        std::filesystem::path p(path);
        if (std::filesystem::is_directory(p))
        {
            return path;
        }
        return p.parent_path().wstring();
    }

    void CoreCLRHost::BuildTpaList(const std::wstring& directory, std::wstring& tpaList)
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

                std::wstring extension = entry.path().extension().wstring();
                if (extension == L".dll" || extension == L".exe")
                {
                    if (!tpaList.empty())
                    {
                        tpaList += L";";
                    }
                    tpaList += entry.path().wstring();
                }
            }
        }
        catch (...)
        {
            // Ignore filesystem errors
        }
    }
}
