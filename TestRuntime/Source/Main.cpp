// Copyright (c) 2025 Evangelion Manuhutu

#include "Entity.h"
#include "EntityManager.h"
#include "ScriptBindings.h"

#include "CoreCLRHost.h"

#include <iostream>
#include <print>
#include <filesystem>
#include <string>
#include <windows.h>
#include <thread>
#include <chrono>

// Entity lifecycle delegates
typedef void (CORECLR_DELEGATE_CALLTYPE *EntityStartDelegate)(uint64_t entityID);
typedef void (CORECLR_DELEGATE_CALLTYPE *EntityUpdateDelegate)(uint64_t entityID, float deltaTime);
typedef void (CORECLR_DELEGATE_CALLTYPE *EntityStopDelegate)(uint64_t entityID);

// Internal call replacement delegates
typedef void (CORECLR_DELEGATE_CALLTYPE *Entity_GetTransformDelegate)(uint64_t entityID, test::TransformComponent* outTransform);
typedef void (CORECLR_DELEGATE_CALLTYPE *Entity_SetTransformDelegate)(uint64_t entityID, test::TransformComponent* transform);
typedef bool (CORECLR_DELEGATE_CALLTYPE *Entity_HasComponentDelegate)(uint64_t entityID, const char* componentType);
typedef void (CORECLR_DELEGATE_CALLTYPE *LogDelegate)(const char* message);
typedef int (CORECLR_DELEGATE_CALLTYPE *DescribeTypeDelegate)(const char* typeName, char* buffer, int bufferSize);

void TestEntitySystem(mochi::CoreCLRHostAPI* host)
{
    std::println("\n----- Entity Component System Test -----");

	// Create entity manager
	test::EntityManager entityManager;
	entityManager.Initialize();

	// Create entities
	test::Entity* player = entityManager.CreateEntity("Player");
	player->transform.position = { 0.0f, 0.0f, 0.0f };

	// Use MochiSharp.Managed for core engine functionality
	const char* ManagedDLLName = "MochiSharp.Managed";
	const char* EntityBridgeClassName = "MochiSharp.Managed.Core.EntityBridge";
	const char* InternalCallsClassName = "MochiSharp.Managed.Core.InternalCalls";
	const char* ReflectionBridgeClassName = "MochiSharp.Managed.Core.ReflectionBridge";

	// Initialize internal call delegates (C++ -> C# property setters)
    std::println("Initializing internal call system..");
	
	//  Create C++ function wrappers that will be called from C#
	typedef void (CORECLR_DELEGATE_CALLTYPE *SetGetTransformDelegateFunc)(Entity_GetTransformDelegate);
	typedef void (CORECLR_DELEGATE_CALLTYPE *SetSetTransformDelegateFunc)(Entity_SetTransformDelegate);
	
	SetGetTransformDelegateFunc setGetTransformDelegate = nullptr;
	SetSetTransformDelegateFunc setSetTransformDelegate = nullptr;
	DescribeTypeDelegate describeTypeDelegate = nullptr;
	
	// Create delegates to set the C# delegate properties from MochiSharp.Managed
    if (!host->CreateDelegate(ManagedDLLName, InternalCallsClassName, "set_Entity_GetTransform", (void **)(&setGetTransformDelegate)))
    {
        std::println("Failed to create set_Entity_GetTransform delegate");
    }
    else
    {
        // Create our C++ implementation delegate
        Entity_GetTransformDelegate getTransformImpl = [](uint64_t entityID, test::TransformComponent *outTransform)
        {
            test::ScriptBindings::Entity_GetTransform(entityID, outTransform);
        };
        setGetTransformDelegate(getTransformImpl);
        std::println("Entity_GetTransform initialized!");
    }
	
    if (!host->CreateDelegate(ManagedDLLName, InternalCallsClassName, "set_Entity_SetTransform", (void **)(&setSetTransformDelegate)))
    {
        std::println("Failed to create set_Entity_SetTransform delegate");
    }
    else
    {
        // Create our C++ implementation delegate
        Entity_SetTransformDelegate setTransformImpl = [](uint64_t entityID, test::TransformComponent *transform)
        {
            test::ScriptBindings::Entity_SetTransform(entityID, transform);
        };
        setSetTransformDelegate(setTransformImpl);
        std::println("Entity_SetTransform initialized!");
    }

	// Managed reflection helper for script metadata
	if (!host->CreateDelegate(ManagedDLLName, ReflectionBridgeClassName, "DescribeType", (void**)(&describeTypeDelegate)))
	{
		std::println("Failed to create DescribeType delegate");
	}
	else
	{
		std::println("DescribeType delegate initialized!");
	}

	auto logTypeMetadata = [&](const char* typeName)
	{
		if (!describeTypeDelegate)
		{
			return;
		}

		int requiredSize = describeTypeDelegate(typeName, nullptr, 0);
		if (requiredSize <= 0)
		{
			std::println("DescribeType returned invalid size for {}", typeName);
			return;
		}

		std::string buffer(static_cast<size_t>(requiredSize), '\0');
		describeTypeDelegate(typeName, buffer.data(), requiredSize);
		std::println("Type metadata for {}: {}", typeName, buffer.c_str());
	};

	// Create delegates for lifecycle methods (using static bridge methods)
	EntityStartDelegate startDelegate = nullptr;
	EntityUpdateDelegate updateDelegate = nullptr;
	EntityStopDelegate stopDelegate = nullptr;

	if (!host->CreateDelegate(ManagedDLLName, EntityBridgeClassName, "Start", (void**)(&startDelegate)))
	{
		std::println("Failed to create Start delegate");
		entityManager.Shutdown();
		return;
	}

	if (!host->CreateDelegate(ManagedDLLName, EntityBridgeClassName, "Update", (void**)(&updateDelegate)))
	{
		std::println("Failed to create Update delegate");
		entityManager.Shutdown();
		return;
	}

	if (!host->CreateDelegate(ManagedDLLName, EntityBridgeClassName, "Stop", (void**)(&stopDelegate)))
	{
		std::println("Failed to create Stop delegate");
		entityManager.Shutdown();
		return;
	}

    std::println("Successfully created all entity lifecycle delegates!");

	// Dump managed metadata for the target script type
	logTypeMetadata("TestScript.Scene.PlayerController");

	// Create entity instance in C# (using EntityBridge)
	typedef void (CORECLR_DELEGATE_CALLTYPE *CreateEntityInstanceDelegate)(uint64_t entityID, const char* typeName);
	CreateEntityInstanceDelegate createInstanceDelegate = nullptr;
	
	
    if (host->CreateDelegate(ManagedDLLName, EntityBridgeClassName, "CreateEntityInstance", (void **)(&createInstanceDelegate)))
    {
        std::println("Calling CreateEntityInstance with ID={}, Type={}", player->id, "TestScript.Scene.PlayerController");
        createInstanceDelegate(player->id, "TestScript.Scene.PlayerController");
        std::println("CreateEntityInstance completed");
    }
	else
	{
        std::println("Failed to create CreateEntityInstance delegate");
		entityManager.Shutdown();
		return;
	}

	// Add a small delay to let C# logging flush
	std::this_thread::sleep_for(std::chrono::milliseconds(100));

	// Attach script to entity
	entityManager.AttachScript(player->id, "TestScript.Scene.PlayerController", nullptr);
	
	// Set the delegates for the script
	entityManager.SetScriptDelegates(player->id, startDelegate, updateDelegate, stopDelegate);

    std::println("About to call StartEntity..");

	// Start the entity
	entityManager.StartEntity(player->id);

    std::println("StartEntity completed!");

	// Simulate game loop
    std::println("\n--- Simulating game loop for 3 seconds --");

	float deltaTime = 0.016f; // ~60 FPS
	int frameCount = 0;
	int maxFrames = 180; // 3 seconds at 60 FPS

	auto startTime = std::chrono::high_resolution_clock::now();

	while (frameCount < maxFrames)
	{
		entityManager.UpdateAll(deltaTime);

		// Print position every 60 frames (1 second)
		if (frameCount % 60 == 0)
		{
			auto& pos = player->transform.position;
            std::println("Frame {} - x:{} y:{} z:{}", frameCount, pos.x, pos.y, pos.z);
		}

		frameCount++;
		
		// Sleep to simulate frame timing (optional, for readability)
		std::this_thread::sleep_for(std::chrono::milliseconds(16));
	}

	auto endTime = std::chrono::high_resolution_clock::now();
	auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime).count();
	
    std::println("\n--- Game loop finished ---");
	std::println("Total frames: {}",frameCount);
    std::println("Duration: {}ms", duration);

	// Stop the entity
	entityManager.StopEntity(player->id);

	// Unregister entity from C#
	typedef void (CORECLR_DELEGATE_CALLTYPE *UnregisterEntityDelegate)(uint64_t entityID);
	UnregisterEntityDelegate unregisterDelegate = nullptr;
	if (host->CreateDelegate(ManagedDLLName, EntityBridgeClassName, "UnregisterEntity", (void**)(&unregisterDelegate)))
	{
		unregisterDelegate(player->id);
	}

	// IMPORTANT: Clear C# delegate references before shutdown
    std::println("Clearing C# delegate references...");
	
	// Clear EntityBridge dictionary
	typedef void (CORECLR_DELEGATE_CALLTYPE *ClearEntityBridgeDelegate)();
	ClearEntityBridgeDelegate clearEntityBridge = nullptr;
    if (host->CreateDelegate(ManagedDLLName, EntityBridgeClassName, "ClearAll", (void **)(&clearEntityBridge)))
    {
        clearEntityBridge();
        std::println("EntityBridge cleared");
    }
	
	// Clear internal call delegates to prevent dangling pointers
	typedef void (CORECLR_DELEGATE_CALLTYPE *ClearInternalCallsDelegate)();
	ClearInternalCallsDelegate clearInternalCalls = nullptr;
    if (host->CreateDelegate(ManagedDLLName, InternalCallsClassName, "ClearDelegates", (void **)(&clearInternalCalls)))
    {
        clearInternalCalls();
        std::println("Internal call delegates cleared");
    }

	// Cleanup C++ side
	entityManager.Shutdown();
	
    std::println("Entity system shutdown complete");
}

int main()
{
	const std::string dllName = "MochiSharp.Native.dll";
    HMODULE hCoreDll = LoadLibraryA(dllName.c_str());
    if (!hCoreDll)
    {
    	std::println("Failed to load {}", dllName);
        return 1;
    }

    std::string runtimePath = R"(C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.1)";
    std::string assemblyPath = std::filesystem::current_path().string() + "\\MochiSharp.Managed.dll";

    // Check if paths exist
    bool pathsExist = true;
    if (!std::filesystem::exists(runtimePath))
    {
    	std::println("Runtime path does not exist: {}", runtimePath);
    	std::println("Please update the runtimePath variable with your .NET installation path.");
        pathsExist = false;
    }

    if (!std::filesystem::exists(assemblyPath))
    {
    	std::println("Assembly path does not exist: {}", assemblyPath);
    	std::println("Please build MochiSharp.Managed project or update the assemblyPath variable.");
        pathsExist = false;
    }

    if (!pathsExist)
    {
        FreeLibrary(hCoreDll);
        return 1;
    }

    // Option 1: Using factory function with settings
    typedef mochi::CoreCLRHostAPI* (*CreateCoreRuntimeHostWithSettingsFunc)(const char*, const char*, const char*);
    typedef void (*DestroyCoreRuntimeHostFunc)(mochi::CoreCLRHostAPI*);

    auto CreateCoreRuntimeHostWithSettings = reinterpret_cast<CreateCoreRuntimeHostWithSettingsFunc>(GetProcAddress(hCoreDll, "CreateCoreRuntimeHostWithSettings"));
    auto DestroyCoreRuntimeHost = reinterpret_cast<DestroyCoreRuntimeHostFunc>(GetProcAddress(hCoreDll, "DestroyCoreRuntimeHost"));

    if (!CreateCoreRuntimeHostWithSettings || !DestroyCoreRuntimeHost)
    {
    	std::println("Failed to get factory functions from {}", dllName);
        FreeLibrary(hCoreDll);
        return 1;
    }

    // Create the CoreCLR host with settings
	std::println("Creating CoreCLR host with settings");
    mochi::CoreCLRHostAPI* host = CreateCoreRuntimeHostWithSettings(runtimePath.c_str(), assemblyPath.c_str(), "MochiSharpHost");
    
    if (!host)
    {
    	std::println("Failed to create CoreCLR host");
        FreeLibrary(hCoreDll);
        return 1;
    }

    // Initialize CoreCLR using the settings passed in constructor
	std::println("Initializing CoreCLR");
    if (!host->Initialize())
    {
    	std::println("Failed to initialize CoreCLR runtime");
        DestroyCoreRuntimeHost(host);
        FreeLibrary(hCoreDll);
        return 1;
    }

    TestEntitySystem(host);

    // Shutdown
	std::println("Shutting down...");

    // Give C# GC a chance to finalize objects
	std::println("Waiting for GC...");
    std::this_thread::sleep_for(std::chrono::milliseconds(1000));

    // Shutdown CoreCLR & Destroy host
    host->Shutdown();
    DestroyCoreRuntimeHost(host);
    
	std::println("Unloading {}...", dllName);
    FreeLibrary(hCoreDll);
    return 0;
}

