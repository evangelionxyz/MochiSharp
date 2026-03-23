// Copyright (c) 2025 Evangelion Manuhutu

#ifndef HOST_H
#define HOST_H

#ifdef _WIN32
    #include <Windows.h>
#else
    #include <dlfcn.h>
#endif

#include <vector>
#include <iostream>
#include <string>
#include <filesystem>

#include <nethost.h>

#include <coreclr_delegates.h>
#include <hostfxr.h>

extern hostfxr_initialize_for_runtime_config_fn init_fptr;
extern hostfxr_get_runtime_delegate_fn get_delegate_fptr;
extern hostfxr_close_fn close_fptr;

namespace MochiSharp
{
    struct EngineInterface
    {
        typedef void (*LogFunc)(const char *message);
        LogFunc LogMessage;
    };

    typedef int (CORECLR_DELEGATE_CALLTYPE *InitializeFn)(EngineInterface *engineApi);
    typedef int (CORECLR_DELEGATE_CALLTYPE *LoadAssemblyFn)(const char *path);
    typedef int (CORECLR_DELEGATE_CALLTYPE *RegisterSignatureFn)(int signatureId, const char *returnTypeName, const char **parameterTypeNames, int parameterCount);
    typedef int (CORECLR_DELEGATE_CALLTYPE *CreateInstanceFn)(const char *typeName, uint64_t instanceId);
    typedef void (CORECLR_DELEGATE_CALLTYPE *DestroyInstanceFn)(uint64_t instanceId);
    typedef int (CORECLR_DELEGATE_CALLTYPE *BindInstanceMethodFn)(uint64_t instanceId, const char *methodName, int signature);
    typedef int (CORECLR_DELEGATE_CALLTYPE *BindStaticMethodFn)(const char *typeName, const char *methodName, int signature);
    typedef int (CORECLR_DELEGATE_CALLTYPE *InvokeFn)(int methodId, const void *argsPtr, int argCount, void *returnPtr);
    typedef const char *(CORECLR_DELEGATE_CALLTYPE *GetDerivedTypesFn)(const char *asmPath, const char *baseType);

    struct HostSettings
    {
    };

    class DotNetHost
    {
    private:
        hostfxr_handle m_Ctx = nullptr;
        std::filesystem::path m_BaseDir;
        InitializeFn ManagedInit = nullptr;
        LoadAssemblyFn ManagedLoadAssembly = nullptr;
        RegisterSignatureFn ManagedRegisterSignature = nullptr;
        CreateInstanceFn ManagedCreateInstance = nullptr;
        DestroyInstanceFn ManagedDestroyInstance = nullptr;
        BindInstanceMethodFn ManagedBindInstanceMethod = nullptr;
        BindStaticMethodFn ManagedBindStaticMethod = nullptr;
        InvokeFn ManagedInvoke = nullptr;
        GetDerivedTypesFn ManagedGetDerivedTypes = nullptr;

    public:
        static void EngineLog(const char *msg);
        bool Init(const std::wstring &configPath);
        bool LoadAssembly(const char *path);
        bool RegisterSignature(int signatureId, const char *returnTypeName, const char **parameterTypeNames, int parameterCount);
		bool CreateInstance(const char *typeName, uint64_t instanceId);
        void DestroyInstance(uint64_t instanceId);
        int BindInstanceMethod(uint64_t instanceId, const char *methodName, int signature);
        int BindStaticMethod(const char *typeName, const char *methodName, int signature);
        bool Invoke(int methodId, const void *argsPtr, int argCount, void *returnPtr);
        std::string GetDerivedTypes(const char *asmPath, const char *baseType);
    private:
        bool LoadHostFxr();
    };
}

#endif // !HOST_H
