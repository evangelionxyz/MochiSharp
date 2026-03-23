// Copyright (c) 2025 Evangelion Manuhutu

#include "Host.h"
#include <iostream>
#include <iomanip>
#include <assert.h>
#include <sstream>
#ifdef _WIN32
#include <combaseapi.h>
#endif

#define STR(s) L ## s
#define CH(c) L ## c
#define DIR_SEPARATOR L'\\'

hostfxr_initialize_for_runtime_config_fn init_fptr = nullptr;
hostfxr_get_runtime_delegate_fn get_delegate_fptr = nullptr;
hostfxr_close_fn close_fptr = nullptr;

#include <filesystem>

static std::filesystem::path GetExecutablePath()
{
    wchar_t buffer[MAX_PATH];
    DWORD len = GetModuleFileNameW(nullptr, buffer, MAX_PATH);
    if (len == 0)
    {
        return std::filesystem::current_path();
    }
    return std::filesystem::path(buffer);
}

static void EmitDebugEvent(const std::string &eventPayload)
{
    HANDLE pipe = CreateFileW(
        L"\\\\.\\pipe\\ignite-debug-events",
        GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr);

    if (pipe == INVALID_HANDLE_VALUE)
    {
        return;
    }

    std::string line = eventPayload + "\n";
    DWORD bytesWritten = 0;
    WriteFile(pipe, line.c_str(), (DWORD)line.size(), &bytesWritten, nullptr);
    FlushFileBuffers(pipe);
    CloseHandle(pipe);
}

static void EmitRuntimeStartedEvent()
{
    std::ostringstream payload;
    payload << "event=runtime-started;pid=" << GetCurrentProcessId();
    EmitDebugEvent(payload.str());
}

static void EmitAssemblyLoadedEvent(const std::filesystem::path &assemblyPath)
{
    std::ostringstream payload;
    payload << "event=assembly-loaded;pid=" << GetCurrentProcessId() << ";path=" << assemblyPath.string();
    EmitDebugEvent(payload.str());
}

static std::filesystem::path GetExecutableDirectory()
{
    auto exePath = GetExecutablePath();
    return exePath.has_parent_path() ? exePath.parent_path() : std::filesystem::current_path();
}

static std::filesystem::path ResolvePathRelativeToExecutable(const std::filesystem::path &path)
{
    if (path.is_absolute())
    {
        return path;
    }

    auto exeDir = GetExecutableDirectory();
    auto candidate = exeDir / path;
    if (std::filesystem::exists(candidate))
    {
        return candidate;
    }

    return std::filesystem::current_path() / path;
}

namespace MochiSharp
{
    void DotNetHost::EngineLog(const char *msg)
    {
        std::cout << "[C++ Engine] " << msg << "\n";
    }

    bool DotNetHost::Init(const std::wstring &configPath)
    {
        if (!LoadHostFxr())
        {
            return false;
        }

        auto configFullPath = ResolvePathRelativeToExecutable(std::filesystem::path(configPath));
        if (!std::filesystem::exists(configFullPath))
        {
            std::wcout << L"[C++ Engine] runtimeconfig not found: " << configFullPath.wstring() << L"\n";
            return false;
        }

        m_BaseDir = configFullPath.parent_path();

        int rc = init_fptr(configFullPath.c_str(), nullptr, &m_Ctx);
        if (rc != 0 || m_Ctx == nullptr)
        {
            return false;
        }

        load_assembly_and_get_function_pointer_fn load_assembly_and_get_function_pointer = nullptr;
        rc = get_delegate_fptr(
            m_Ctx,
            hdt_load_assembly_and_get_function_pointer,
            (void **)&load_assembly_and_get_function_pointer);

        if (rc != 0 || load_assembly_and_get_function_pointer == nullptr)
        {
            return false;
        }

        // Load ManagedCore and get the function pointers
        auto managedCorePath = (m_BaseDir / L"MochiSharp.Managed.dll");
        if (!std::filesystem::exists(managedCorePath))
        {
            std::wcout << L"[C++ Engine] MochiSharp.Managed.dll not found: " << managedCorePath.wstring() << L"\n";
            return false;
        }

        // Get Initialize
        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("Initialize"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedInit);

        if (rc != 0 || ManagedInit == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load Initialize function (rc: 0x" << std::hex << rc << std::dec << ")\n";
            return false;
        }

        // Get LoadAssembly
        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("LoadAssembly"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedLoadAssembly);

        if (rc != 0 || ManagedLoadAssembly == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load LoadAssembly function (rc: 0x" << std::hex << rc << std::dec << ")\n";
            return false;
        }

        // Get RegisterSignature
        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("RegisterSignature"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedRegisterSignature);

        if (rc != 0 || ManagedRegisterSignature == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load RegisterSignature function (rc: 0x" << std::hex << rc << std::dec << ")\n";
            return false;
        }

        // Get CreateInstance
        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("CreateInstance"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedCreateInstance);

        if (rc != 0 || ManagedCreateInstance == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load CreateInstanceGuid function (rc: 0x" << std::hex << rc << std::dec << ")\n";
            return false;
        }

        // Get DestroyInstance
        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("DestroyInstance"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedDestroyInstance);

        if (rc != 0 || ManagedDestroyInstance == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load DestroyInstance function (rc: 0x" << std::hex << rc << std::dec << ")\n";
            return false;
        }

        // Get BindInstanceMethod
        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("BindInstanceMethod"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedBindInstanceMethod);

        if (rc != 0 || ManagedBindInstanceMethod == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load BindInstanceMethod function (rc: 0x" << std::hex << rc << std::dec << ")\n";
            return false;
        }

        // Get BindStaticMethod
        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("BindStaticMethod"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedBindStaticMethod);

        if (rc != 0 || ManagedBindStaticMethod == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load BindStaticMethod function (rc: 0x" << std::hex << rc << std::dec << ")\n";
            return false;
        }

        // Get Invoke
        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("Invoke"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedInvoke);

        if (rc != 0 || ManagedInvoke == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load Invoke function (rc: 0x" << std::hex << rc << std::dec << ")\n";
            return false;
        }

        rc = load_assembly_and_get_function_pointer(
            managedCorePath.c_str(),
            STR("MochiSharp.Managed.Core.Bootstrap, MochiSharp.Managed"),
            STR("GetDerivedTypes"),
            UNMANAGEDCALLERSONLY_METHOD,
            nullptr,
            (void **)&ManagedGetDerivedTypes);

        if (rc != 0 || ManagedGetDerivedTypes == nullptr)
        {
            std::cout << "[C++ Engine] Failed to load GetDerivedTypes function (rc: 0x" << std::hex << rc << std::dec << ")\n";
        }

        // Call Initialize
        EngineInterface api;
        api.LogMessage = &EngineLog;
        ManagedInit(&api);
        EmitRuntimeStartedEvent();

        return true;
    }

    bool DotNetHost::LoadAssembly(const char *path)
    {
        if (!ManagedLoadAssembly)
        {
            return false;
        }

        std::filesystem::path scriptPath(path);
        if (!scriptPath.is_absolute())
        {
            scriptPath = m_BaseDir / scriptPath;
        }

        auto resolved = scriptPath.string();
        bool loaded = ManagedLoadAssembly(resolved.c_str()) != 0;
        if (loaded)
        {
            EmitAssemblyLoadedEvent(scriptPath);
        }

        return loaded;
    }

    bool DotNetHost::RegisterSignature(int signatureId, const char *returnTypeName, const char **parameterTypeNames, int parameterCount)
    {
        if (!ManagedRegisterSignature)
        {
            return false;
        }

        return ManagedRegisterSignature(signatureId, returnTypeName, parameterTypeNames, parameterCount) != 0;
    }

    bool DotNetHost::CreateInstance(const char *typeName, uint64_t instanceId)
    {
        if (!ManagedCreateInstance)
        {
            return false;
        }

        return ManagedCreateInstance(typeName, instanceId) != 0;
    }

    void DotNetHost::DestroyInstance(uint64_t instanceId)
    {
        if (ManagedDestroyInstance)
        {
            ManagedDestroyInstance(instanceId);
        }
    }

    int DotNetHost::BindInstanceMethod(uint64_t instanceId, const char *methodName, int signature)
    {
        if (!ManagedBindInstanceMethod)
        {
            return 0;
        }

        return ManagedBindInstanceMethod(instanceId, methodName, signature);
    }

    int DotNetHost::BindStaticMethod(const char *typeName, const char *methodName, int signature)
    {
        if (!ManagedBindStaticMethod)
        {
            return 0;
        }

        return ManagedBindStaticMethod(typeName, methodName, signature);
    }

    bool DotNetHost::Invoke(int methodId, const void *argsPtr, int argCount, void *returnPtr)
    {
        if (!ManagedInvoke)
        {
            return false;
        }

        return ManagedInvoke(methodId, argsPtr, argCount, returnPtr) != 0;
    }

    std::string DotNetHost::GetDerivedTypes(const char *asmPath, const char *baseType)
	{
        if (!ManagedGetDerivedTypes)
        {
            return {};
        }

        const char *result = ManagedGetDerivedTypes(asmPath, baseType);
        if (!result)
        {
            return {};
        }

        std::string managedResult(result);
#ifdef _WIN32
        CoTaskMemFree((LPVOID)result);
#endif
        return managedResult;
	}

	bool DotNetHost::LoadHostFxr()
    {
        char_t buffer[MAX_PATH];
        size_t bufferSize = sizeof(buffer) / sizeof(buffer[0]);
        int rc = get_hostfxr_path(buffer, &bufferSize, nullptr);
        if (rc != 0)
        {
            return false;
        }

        HMODULE lib = LoadLibraryW(buffer);
        init_fptr = (hostfxr_initialize_for_runtime_config_fn)GetProcAddress(lib, "hostfxr_initialize_for_runtime_config");
        get_delegate_fptr = (hostfxr_get_runtime_delegate_fn)GetProcAddress(lib, "hostfxr_get_runtime_delegate");
        close_fptr = (hostfxr_close_fn)GetProcAddress(lib, "hostfxr_close");

        return (init_fptr && get_delegate_fptr && close_fptr);
    }

}