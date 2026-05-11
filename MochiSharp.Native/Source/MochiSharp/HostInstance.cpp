#include "PCH.hpp"
#include "HostInstance.hpp"
#include "StringHelper.hpp"
#include "TypeCache.hpp"
#include "String.hpp"

#include "Verify.hpp"
#include "HostFXRErrorCodes.hpp"
#include "MochiManagedFunctions.hpp"

#include "nethost.h"

#ifdef MOCHI_PLATFORM_WINDOWS
#   include <ShlObj_core.h>
#else
#   include <dlfcn.h>
#endif

namespace mochi
{
    struct CoreCLRFunctions
    {
        hostfxr_set_error_writer_fn SetHostFXRErrorWriter = nullptr;
        hostfxr_set_runtime_property_value_fn SetRuntimePropertyValue = nullptr;
        hostfxr_initialize_for_runtime_config_fn InitHostFXRForRuntimeConfig = nullptr;
        hostfxr_get_runtime_delegate_fn GetRuntimeDelegate = nullptr;
        hostfxr_close_fn CloseHostFXR = nullptr;
        load_assembly_and_get_function_pointer_fn GetManagedFunctionPtr = nullptr;
    };
    static CoreCLRFunctions s_CoreCLRFunctions;

    static MessageCallbackFn MessageCallback = nullptr;
    static MessageLevel MessageFilter;
    static ExceptionCallbackFn ExceptionCallback = nullptr;

    static void DefaultMessageCallback(std::string_view InMessage, MessageLevel InLevel)
    {
        const char *level = "";

        switch (InLevel)
        {
            default: break;
            case MessageLevel::Trace:
            level = "Trace";
            break;
            case MessageLevel::Info:
            level = "Info";
            break;
            case MessageLevel::Warning:
            level = "Warn";
            break;
            case MessageLevel::Error:
            level = "Error";
            break;
        }

        std::cout << "[MochiSharp](" << level << "): " << InMessage << std::endl;
    }

    MochiSharpInitStatus HostInstance::Initialize(HostSettings InSettings)
    {
        MOCHI_VERIFY(!m_Initialized);

        if (!LoadHostFXR())
        {
            return MochiSharpInitStatus::DotNetNotFound;
        }

        // Setup settings
        m_Settings = std::move(InSettings);

        if (!m_Settings.MessageCallback)
            m_Settings.MessageCallback = DefaultMessageCallback;
        MessageCallback = m_Settings.MessageCallback;
        MessageFilter = m_Settings.MessageFilter;

        s_CoreCLRFunctions.SetHostFXRErrorWriter([](const UCChar *InMessage)
        {
            auto message = StringHelper::ConvertWideToUtf8(InMessage);
            MessageCallback(message, MessageLevel::Error);
        });

        m_MochiSharpManagedAssemblyPath = std::filesystem::path(m_Settings.MochiSharpDirectory) / "MochiSharp.Managed.dll";

        if (!std::filesystem::exists(m_MochiSharpManagedAssemblyPath))
        {
            MessageCallback("Failed to find MochiSharp.Managed.dll", MessageLevel::Error);
            return MochiSharpInitStatus::MochiSharpManagedNotFound;
        }

        if (!InitializeMochiSharpManaged())
        {
            return MochiSharpInitStatus::MochiSharpManagedInitError;
        }

        return MochiSharpInitStatus::Success;
    }

    void HostInstance::Shutdown()
    {
        s_CoreCLRFunctions.CloseHostFXR(m_HostFXRContext);
    }

    AssemblyLoadContext HostInstance::CreateAssemblyLoadContext(std::string_view InName)
    {
        ScopedString name = String::New(InName);
        ScopedString dllPath = String::New("");
        AssemblyLoadContext alc;
        alc.m_ContextId = s_ManagedFunctions.CreateAssemblyLoadContextFptr(name, dllPath);
        alc.m_Host = this;
        return alc;
    }

    AssemblyLoadContext HostInstance::CreateAssemblyLoadContext(std::string_view InName, std::string_view InDllPath)
    {
        ScopedString name = String::New(InName);
        ScopedString dllPath = String::New(InDllPath);
        AssemblyLoadContext alc;
        alc.m_ContextId = s_ManagedFunctions.CreateAssemblyLoadContextFptr(name, dllPath);
        alc.m_Host = this;
        return alc;
    }

    void HostInstance::UnloadAssemblyLoadContext(AssemblyLoadContext &InLoadContext)
    {
        s_ManagedFunctions.UnloadAssemblyLoadContextFptr(InLoadContext.m_ContextId);
        InLoadContext.m_ContextId = -1;
        InLoadContext.m_LoadedAssemblies.Clear();
    }

#ifdef MOCHI_PLATFORM_WINDOWS
    template <typename TFunc>
    TFunc LoadFunctionPtr(void *InLibraryHandle, const char *InFunctionName)
    {
        auto result = (TFunc)GetProcAddress((HMODULE)InLibraryHandle, InFunctionName);
        MOCHI_VERIFY(result);
        return result;
    }
#else
    template <typename TFunc>
    TFunc LoadFunctionPtr(void *InLibraryHandle, const char *InFunctionName)
    {
        auto result = (TFunc)dlsym(InLibraryHandle, InFunctionName);
        MOCHI_VERIFY(result);
        return result;
    }
#endif

    static std::filesystem::path GetHostFXRPath()
    {
        char_t buffer[MAX_PATH];
        size_t bufferSize = sizeof(buffer) / sizeof(buffer[0]);
        int rc = get_hostfxr_path(buffer, &bufferSize, nullptr);
        if (rc != 0)
            return "";

        return std::filesystem::path(buffer);
    }

    bool HostInstance::LoadHostFXR() const
    {
        // Retrieve the file path to the CoreCLR library
        auto hostfxrPath = GetHostFXRPath();

        if (hostfxrPath.empty())
        {
            return false;
        }

        // Load the CoreCLR library
        void *libraryHandle = nullptr;

#ifdef MOCHI_PLATFORM_WINDOWS
#ifdef MOCHI_WIDE_CHARS
        libraryHandle = LoadLibraryW(hostfxrPath.c_str());
#else
        libraryHandle = LoadLibraryA(hostfxrPath.string().c_str());
#endif
#else
        libraryHandle = dlopen(hostfxrPath.string().data(), RTLD_NOW | RTLD_GLOBAL);
#endif

        if (libraryHandle == nullptr)
        {
            return false;
        }

        // Load CoreCLR functions
        s_CoreCLRFunctions.SetHostFXRErrorWriter = LoadFunctionPtr<hostfxr_set_error_writer_fn>(libraryHandle, "hostfxr_set_error_writer");
        s_CoreCLRFunctions.SetRuntimePropertyValue = LoadFunctionPtr<hostfxr_set_runtime_property_value_fn>(libraryHandle, "hostfxr_set_runtime_property_value");
        s_CoreCLRFunctions.InitHostFXRForRuntimeConfig = LoadFunctionPtr<hostfxr_initialize_for_runtime_config_fn>(libraryHandle, "hostfxr_initialize_for_runtime_config");
        s_CoreCLRFunctions.GetRuntimeDelegate = LoadFunctionPtr<hostfxr_get_runtime_delegate_fn>(libraryHandle, "hostfxr_get_runtime_delegate");
        s_CoreCLRFunctions.CloseHostFXR = LoadFunctionPtr<hostfxr_close_fn>(libraryHandle, "hostfxr_close");

        return true;
    }

    bool HostInstance::InitializeMochiSharpManaged()
    {
        // Fetch load_assembly_and_get_function_pointer_fn from CoreCLR
        {
            auto runtimeConfigPath = std::filesystem::path(m_Settings.MochiSharpDirectory) / "MochiSharp.Managed.runtimeconfig.json";

            if (!std::filesystem::exists(runtimeConfigPath))
            {
                MessageCallback("Failed to find MochiSharp.Managed.runtimeconfig.json", MessageLevel::Error);
                return false;
            }

            int status = s_CoreCLRFunctions.InitHostFXRForRuntimeConfig(runtimeConfigPath.c_str(), nullptr, &m_HostFXRContext);
            MOCHI_VERIFY(status == StatusCode::Success || status == StatusCode::Success_HostAlreadyInitialized || status == StatusCode::Success_DifferentRuntimeProperties);
            MOCHI_VERIFY(m_HostFXRContext != nullptr);

            std::filesystem::path mochiSharplDirectoryPath = m_Settings.MochiSharpDirectory;
            s_CoreCLRFunctions.SetRuntimePropertyValue(m_HostFXRContext, MOCHI_STR("APP_CONTEXT_BASE_DIRECTORY"), mochiSharplDirectoryPath.c_str());

            status = s_CoreCLRFunctions.GetRuntimeDelegate(m_HostFXRContext, hdt_load_assembly_and_get_function_pointer, (void **)&s_CoreCLRFunctions.GetManagedFunctionPtr);
            MOCHI_VERIFY(status == StatusCode::Success);
        }

        using InitializeFn = void(*)(void(*)(String, MessageLevel), void(*)(String));
        InitializeFn mochiSharpManagedEntryPoint = nullptr;
        mochiSharpManagedEntryPoint = LoadMochiSharpManagedFunctionPtr<InitializeFn>(MOCHI_STR("MochiSharp.Managed.ManagedHost, MochiSharp.Managed"), MOCHI_STR("Initialize"));

        LoadMochiSharpFunctions();

        mochiSharpManagedEntryPoint([](String InMessage, MessageLevel InLevel)
        {
            if (MessageFilter & InLevel)
            {
                std::string message = InMessage;
                MessageCallback(message, InLevel);
            }
        },
            [](String InMessage)
        {
            std::string message = InMessage;
            if (!ExceptionCallback)
            {
                MessageCallback(message, MessageLevel::Error);
                return;
            }

            ExceptionCallback(message);
        });

        ExceptionCallback = m_Settings.ExceptionCallback;

        return true;
    }

    void HostInstance::LoadMochiSharpFunctions()
    {
        s_ManagedFunctions.CreateAssemblyLoadContextFptr = LoadMochiSharpManagedFunctionPtr<CreateAssemblyLoadContextFn>(MOCHI_STR("MochiSharp.Managed.AssemblyLoader, MochiSharp.Managed"), MOCHI_STR("CreateAssemblyLoadContext"));
        s_ManagedFunctions.UnloadAssemblyLoadContextFptr = LoadMochiSharpManagedFunctionPtr<UnloadAssemblyLoadContextFn>(MOCHI_STR("MochiSharp.Managed.AssemblyLoader, MochiSharp.Managed"), MOCHI_STR("UnloadAssemblyLoadContext"));
        s_ManagedFunctions.LoadAssemblyFptr = LoadMochiSharpManagedFunctionPtr<LoadAssemblyFn>(MOCHI_STR("MochiSharp.Managed.AssemblyLoader, MochiSharp.Managed"), MOCHI_STR("LoadAssembly"));
        s_ManagedFunctions.LoadAssemblyFromMemoryFptr = LoadMochiSharpManagedFunctionPtr<LoadAssemblyFromMemoryFn>(MOCHI_STR("MochiSharp.Managed.AssemblyLoader, MochiSharp.Managed"), MOCHI_STR("LoadAssemblyFromMemory"));
        s_ManagedFunctions.UnloadAssemblyLoadContextFptr = LoadMochiSharpManagedFunctionPtr<UnloadAssemblyLoadContextFn>(MOCHI_STR("MochiSharp.Managed.AssemblyLoader, MochiSharp.Managed"), MOCHI_STR("UnloadAssemblyLoadContext"));
        s_ManagedFunctions.GetLastLoadStatusFptr = LoadMochiSharpManagedFunctionPtr<GetLastLoadStatusFn>(MOCHI_STR("MochiSharp.Managed.AssemblyLoader, MochiSharp.Managed"), MOCHI_STR("GetLastLoadStatus"));
        s_ManagedFunctions.GetAssemblyNameFptr = LoadMochiSharpManagedFunctionPtr<GetAssemblyNameFn>(MOCHI_STR("MochiSharp.Managed.AssemblyLoader, MochiSharp.Managed"), MOCHI_STR("GetAssemblyName"));

        s_ManagedFunctions.RunMSBuildFptr = LoadMochiSharpManagedFunctionPtr<RunMSBuildFn>(MOCHI_STR("MochiSharp.Managed.MSBuildRunner, MochiSharp.Managed"), MOCHI_STR("Run"));

        s_ManagedFunctions.GetAssemblyTypesFptr = LoadMochiSharpManagedFunctionPtr<GetAssemblyTypesFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetAssemblyTypes"));
        s_ManagedFunctions.GetFullTypeNameFptr = LoadMochiSharpManagedFunctionPtr<GetFullTypeNameFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetFullTypeName"));
        s_ManagedFunctions.GetAssemblyQualifiedNameFptr = LoadMochiSharpManagedFunctionPtr<GetAssemblyQualifiedNameFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetAssemblyQualifiedName"));
        s_ManagedFunctions.GetBaseTypeFptr = LoadMochiSharpManagedFunctionPtr<GetBaseTypeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetBaseType"));
        s_ManagedFunctions.GetInterfaceTypeCountFptr = LoadMochiSharpManagedFunctionPtr<GetInterfaceTypeCountFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetInterfaceTypeCount"));
        s_ManagedFunctions.GetInterfaceTypesFptr = LoadMochiSharpManagedFunctionPtr<GetInterfaceTypesFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetInterfaceTypes"));
        s_ManagedFunctions.GetTypeSizeFptr = LoadMochiSharpManagedFunctionPtr<GetTypeSizeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetTypeSize"));
        s_ManagedFunctions.IsTypeSubclassOfFptr = LoadMochiSharpManagedFunctionPtr<IsTypeSubclassOfFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("IsTypeSubclassOf"));
        s_ManagedFunctions.IsTypeAssignableToFptr = LoadMochiSharpManagedFunctionPtr<IsTypeAssignableToFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("IsTypeAssignableTo"));
        s_ManagedFunctions.IsTypeAssignableFromFptr = LoadMochiSharpManagedFunctionPtr<IsTypeAssignableFromFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("IsTypeAssignableFrom"));
        s_ManagedFunctions.IsTypeSZArrayFptr = LoadMochiSharpManagedFunctionPtr<IsTypeSZArrayFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("IsTypeSZArray"));
        s_ManagedFunctions.GetElementTypeFptr = LoadMochiSharpManagedFunctionPtr<GetElementTypeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetElementType"));
        s_ManagedFunctions.GetTypeMethodsFptr = LoadMochiSharpManagedFunctionPtr<GetTypeMethodsFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetTypeMethods"));
        s_ManagedFunctions.GetTypeFieldsFptr = LoadMochiSharpManagedFunctionPtr<GetTypeFieldsFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetTypeFields"));
        s_ManagedFunctions.GetTypePropertiesFptr = LoadMochiSharpManagedFunctionPtr<GetTypePropertiesFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetTypeProperties"));
        s_ManagedFunctions.HasTypeAttributeFptr = LoadMochiSharpManagedFunctionPtr<HasTypeAttributeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("HasTypeAttribute"));
        s_ManagedFunctions.GetTypeAttributesFptr = LoadMochiSharpManagedFunctionPtr<GetTypeAttributesFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetTypeAttributes"));
        s_ManagedFunctions.GetTypeManagedTypeFptr = LoadMochiSharpManagedFunctionPtr<GetTypeManagedTypeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetTypeManagedType"));
        s_ManagedFunctions.InvokeStaticMethodFptr = LoadMochiSharpManagedFunctionPtr<InvokeStaticMethodFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("InvokeStaticMethod"));
        s_ManagedFunctions.InvokeStaticMethodRetFptr = LoadMochiSharpManagedFunctionPtr<InvokeStaticMethodRetFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("InvokeStaticMethodRet"));

        s_ManagedFunctions.GetMethodInfoNameFptr = LoadMochiSharpManagedFunctionPtr<GetMethodInfoNameFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetMethodInfoName"));
        s_ManagedFunctions.GetMethodInfoReturnTypeFptr = LoadMochiSharpManagedFunctionPtr<GetMethodInfoReturnTypeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetMethodInfoReturnType"));
        s_ManagedFunctions.GetMethodInfoParameterTypesFptr = LoadMochiSharpManagedFunctionPtr<GetMethodInfoParameterTypesFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetMethodInfoParameterTypes"));
        s_ManagedFunctions.GetMethodInfoAccessibilityFptr = LoadMochiSharpManagedFunctionPtr<GetMethodInfoAccessibilityFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetMethodInfoAccessibility"));
        s_ManagedFunctions.GetMethodInfoAttributesFptr = LoadMochiSharpManagedFunctionPtr<GetMethodInfoAttributesFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetMethodInfoAttributes"));

        s_ManagedFunctions.GetFieldInfoNameFptr = LoadMochiSharpManagedFunctionPtr<GetFieldInfoNameFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetFieldInfoName"));
        s_ManagedFunctions.GetFieldInfoTypeFptr = LoadMochiSharpManagedFunctionPtr<GetFieldInfoTypeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetFieldInfoType"));
        s_ManagedFunctions.GetFieldInfoAccessibilityFptr = LoadMochiSharpManagedFunctionPtr<GetFieldInfoAccessibilityFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetFieldInfoAccessibility"));
        s_ManagedFunctions.GetFieldInfoAttributesFptr = LoadMochiSharpManagedFunctionPtr<GetFieldInfoAttributesFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetFieldInfoAttributes"));

        s_ManagedFunctions.GetPropertyInfoNameFptr = LoadMochiSharpManagedFunctionPtr<GetPropertyInfoNameFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetPropertyInfoName"));
        s_ManagedFunctions.GetPropertyInfoTypeFptr = LoadMochiSharpManagedFunctionPtr<GetPropertyInfoTypeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetPropertyInfoType"));
        s_ManagedFunctions.GetPropertyInfoAttributesFptr = LoadMochiSharpManagedFunctionPtr<GetPropertyInfoAttributesFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetPropertyInfoAttributes"));

        s_ManagedFunctions.GetAttributeFieldValueFptr = LoadMochiSharpManagedFunctionPtr<GetAttributeFieldValueFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetAttributeFieldValue"));
        s_ManagedFunctions.GetAttributeTypeFptr = LoadMochiSharpManagedFunctionPtr<GetAttributeTypeFn>(MOCHI_STR("MochiSharp.Managed.TypeInterface, MochiSharp.Managed"), MOCHI_STR("GetAttributeType"));

        s_ManagedFunctions.SetInternalCallsFptr = LoadMochiSharpManagedFunctionPtr<SetInternalCallsFn>(MOCHI_STR("MochiSharp.Managed.Interop.InternalCallsManager, MochiSharp.Managed"), MOCHI_STR("SetInternalCalls"));
        s_ManagedFunctions.CreateObjectFptr = LoadMochiSharpManagedFunctionPtr<CreateObjectFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("CreateObject"));
        s_ManagedFunctions.CopyObjectFptr = LoadMochiSharpManagedFunctionPtr<CopyObjectFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("CopyObject"));
        s_ManagedFunctions.InvokeMethodFptr = LoadMochiSharpManagedFunctionPtr<InvokeMethodFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("InvokeMethod"));
        s_ManagedFunctions.InvokeMethodRetFptr = LoadMochiSharpManagedFunctionPtr<InvokeMethodRetFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("InvokeMethodRet"));
        s_ManagedFunctions.SetFieldValueFptr = LoadMochiSharpManagedFunctionPtr<SetFieldValueFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("SetFieldValue"));
        s_ManagedFunctions.GetFieldValueFptr = LoadMochiSharpManagedFunctionPtr<GetFieldValueFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("GetFieldValue"));
        s_ManagedFunctions.SetPropertyValueFptr = LoadMochiSharpManagedFunctionPtr<SetFieldValueFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("SetPropertyValue"));
        s_ManagedFunctions.GetPropertyValueFptr = LoadMochiSharpManagedFunctionPtr<GetFieldValueFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("GetPropertyValue"));
        s_ManagedFunctions.DestroyObjectFptr = LoadMochiSharpManagedFunctionPtr<DestroyObjectFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("DestroyObject"));
        s_ManagedFunctions.GetObjectTypeIdFptr = LoadMochiSharpManagedFunctionPtr<GetObjectTypeIdFn>(MOCHI_STR("MochiSharp.Managed.ManagedObject, MochiSharp.Managed"), MOCHI_STR("GetObjectTypeId"));

        s_ManagedFunctions.CollectGarbageFptr = LoadMochiSharpManagedFunctionPtr<CollectGarbageFn>(MOCHI_STR("MochiSharp.Managed.GarbageCollector, MochiSharp.Managed"), MOCHI_STR("CollectGarbage"));
        s_ManagedFunctions.WaitForPendingFinalizersFptr = LoadMochiSharpManagedFunctionPtr<WaitForPendingFinalizersFn>(MOCHI_STR("MochiSharp.Managed.GarbageCollector, MochiSharp.Managed"), MOCHI_STR("WaitForPendingFinalizers"));
    }

    void *HostInstance::LoadMochiSharpManagedFunctionPtr(const std::filesystem::path &InAssemblyPath, const UCChar *InTypeName, const UCChar *InMethodName, const UCChar *InDelegateType) const
    {
        void *funcPtr = nullptr;

        int status = s_CoreCLRFunctions.GetManagedFunctionPtr(InAssemblyPath.c_str(), InTypeName, InMethodName, InDelegateType, nullptr, &funcPtr);
        if (status != StatusCode::Success || !funcPtr)
        {
#ifdef MOCHI_WIDE_CHARS
            std::wcerr << "Failed to retrieve managed function pointer `" << InTypeName << "`::`" << InMethodName << "` from `" << InAssemblyPath << "`" << std::endl;
#else
            std::cerr << "Failed to retrieve managed function pointer `" << InTypeName << "`::`" << InMethodName << "` from `" << InAssemblyPath << "`" << std::endl;
#endif
            MOCHI_VERIFY(false);
        }

        return funcPtr;
    }
}