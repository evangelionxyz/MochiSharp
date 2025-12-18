// Copyright (c) 2025 Evangelion Manuhutu

#include "pch.h"
#include "Base.h"
#include "CoreCLRHost.h"

using namespace criollo;

static CoreCLRHost* g_pCoreHost = nullptr;

extern "C"
{
    CRIOLLO_API bool InitializeCoreRuntime(const char* runtimePath, const char* assemblyPath)
    {
        if (g_pCoreHost != nullptr)
        {
            return false; // Already initialized
        }

        g_pCoreHost = new CoreCLRHost();
        if (!g_pCoreHost->Initialize(runtimePath, assemblyPath))
        {
            delete g_pCoreHost;
            g_pCoreHost = nullptr;
            return false;
        }

        return true;
    }

    // Example function to shut down CoreCLR
    CRIOLLO_API void ShutdownCoreRuntime()
    {
        if (g_pCoreHost != nullptr)
        {
            g_pCoreHost->Shutdown();
            delete g_pCoreHost;
            g_pCoreHost = nullptr;
        }
    }

    CRIOLLO_API bool ExecuteManagedAssembly(const char* assemblyPath)
    {
        if (g_pCoreHost == nullptr || !g_pCoreHost->IsInitialized())
        {
            return false;
        }

        unsigned int exitCode = 0;
        return g_pCoreHost->ExecuteAssembly(assemblyPath, 0, nullptr, &exitCode);
    }

    // Example function to get the host instance (for creating delegates)
    CRIOLLO_API CoreCLRHost* GetCoreHost()
    {
        return g_pCoreHost;
    }

    // Helper function to create a delegate for TestMethod specifically
    // This demonstrates how to create type-safe exported functions for specific delegates
    CRIOLLO_API bool CreateTestMethodDelegate(void** outDelegate)
    {
        if (g_pCoreHost == nullptr || !g_pCoreHost->IsInitialized() || outDelegate == nullptr)
        {
            return false;
        }

        return g_pCoreHost->CreateDelegate(
            "TestScript",           // Assembly name (without .dll extension)
            "Criollo.Test",         // Fully qualified type name
            "TestMethod",           // Method name
            outDelegate
        );
    }

    // Generic helper function to create any delegate
    CRIOLLO_API bool CreateManagedDelegate(const char* assemblyName, const char* typeName, const char* methodName, void** outDelegate)
    {
        if (g_pCoreHost == nullptr || !g_pCoreHost->IsInitialized() ||
            outDelegate == nullptr || assemblyName == nullptr ||
            typeName == nullptr || methodName == nullptr)
        {
            return false;
        }

        return g_pCoreHost->CreateDelegate(
            assemblyName,
            typeName,
            methodName,
            outDelegate
        );
    }
}
