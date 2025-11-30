// Copyright (c) 2025 Evangelion Manuhutu

#ifndef CRIOLLO_SCRIPTING_H
#define CRIOLLO_SCRIPTING_H

#include <windows.h>
#include <string>

#include "Base.h"

namespace criollo
{
    typedef bool (*PFN_InitializeCoreRuntime)(const wchar_t*, const wchar_t*);
    typedef void (*PFN_ShutdownCoreRuntime)();
    typedef bool (*PFN_CreateTestMethodDelegate)(void**);
    typedef bool (*PFN_CreateManagedDelegate)(const char*, const char*, const char*, void**);

    class ScriptEngine
    {
    public:
        ScriptEngine();
        ~ScriptEngine();

        bool LoadCoreLibrary(const std::wstring& dllPath = L"CriolloCore.dll");
        bool Initialize(const std::wstring& runtimePath, const std::wstring& assemblyPath);

        // Create a delegate to a managed method
        template<typename TDelegate>
        bool CreateDelegate(
            const char* assemblyName,
            const char* typeName,
            const char* methodName,
            TDelegate** outDelegate)
        {
            if (!m_pfnCreateDelegate || !m_isInitialized)
                return false;

            return m_pfnCreateDelegate(
                assemblyName,
                typeName,
                methodName,
                reinterpret_cast<void**>(outDelegate)
            );
        }

        // Shutdown the runtime
        void Shutdown();
        bool IsInitialized() const { return m_isInitialized; }

    private:
        HMODULE m_hCoreDll;
        PFN_InitializeCoreRuntime m_pfnInitialize;
        PFN_ShutdownCoreRuntime m_pfnShutdown;
        PFN_CreateManagedDelegate m_pfnCreateDelegate;
        bool m_isInitialized;
    };

    /*
    // Example usage:
    Criollo::Scripting::ScriptEngine engine;

    if (!engine.LoadCoreLibrary())
    {
        // Handle error
    }

    if (!engine.Initialize(runtimePath, assemblyPath))
    {
        // Handle error
    }

    // Create and call delegate
    Criollo::Scripting::TestMethodDelegate testMethod = nullptr;
    if (engine.CreateDelegate("CriolloManaged", "Criollo.Core", "TestMethod", &testMethod))
    {
        int result = testMethod();
    }

    engine.Shutdown();
    */
}

#endif