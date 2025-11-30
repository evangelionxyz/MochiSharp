// Copyright (c) 2025 Evangelion Manuhutu

#include "Entity.h"
#include "EntityManager.h"
#include "ScriptBindings.h"

#include <iostream>
#include <filesystem>
#include <windows.h>
#include <thread>
#include <chrono>

typedef bool (*InitializeCoreRuntimeFunc)(const wchar_t*, const wchar_t*);
typedef void (*ShutdownCoreRuntimeFunc)();
typedef bool (*ExecuteManagedAssemblyFunc)(const wchar_t*);
typedef bool (*CreateTestMethodDelegateFunc)(void**);
typedef bool (*CreateManagedDelegateFunc)(const char*, const char*, const char*, void**);

#if defined(_WIN32)
	#ifndef CORECLR_DELEGATE_CALLTYPE
		#define CORECLR_DELEGATE_CALLTYPE __stdcall
	#endif
#else
	#ifndef CORECLR_DELEGATE_CALLTYPE
		#define CORECLR_DELEGATE_CALLTYPE
	#endif
#endif

typedef int (CORECLR_DELEGATE_CALLTYPE *TestMethodDelegate)();
typedef int (CORECLR_DELEGATE_CALLTYPE *AddDelegate)(int, int);
typedef const char* (CORECLR_DELEGATE_CALLTYPE *GreetDelegate)(const char*);
typedef void (CORECLR_DELEGATE_CALLTYPE *LogMessageDelegate)(const char*);

// Entity lifecycle delegates
typedef void (CORECLR_DELEGATE_CALLTYPE *EntityStartDelegate)(uint64_t entityID);
typedef void (CORECLR_DELEGATE_CALLTYPE *EntityUpdateDelegate)(uint64_t entityID, float deltaTime);
typedef void (CORECLR_DELEGATE_CALLTYPE *EntityStopDelegate)(uint64_t entityID);

// Internal call replacement delegates
typedef void (CORECLR_DELEGATE_CALLTYPE *Entity_GetTransformDelegate)(uint64_t entityID, criollo::TransformComponent* outTransform);
typedef void (CORECLR_DELEGATE_CALLTYPE *Entity_SetTransformDelegate)(uint64_t entityID, criollo::TransformComponent* transform);
typedef bool (CORECLR_DELEGATE_CALLTYPE *Entity_HasComponentDelegate)(uint64_t entityID, const char* componentType);
typedef void (CORECLR_DELEGATE_CALLTYPE *LogDelegate)(const char* message);

void TestBasicFunctionality(CreateManagedDelegateFunc CreateManagedDelegate)
{
	const char* TestAppDLLName = "TestScript";
	const char* TestClassName = "TestScript.Core.Test";

	// Example 1: Call TestMethod
	TestMethodDelegate testMethod = nullptr;
	if (CreateManagedDelegate(TestAppDLLName, TestClassName, "TestMethod", (void**)(&testMethod)) && testMethod)
	{
		std::wcout << L"[Example 1] Calling TestMethod():" << std::endl;
		int result = testMethod();
		std::wcout << L"Result: " << result << std::endl << std::endl;
	}

	// Example 2: Call Add method with parameters
	AddDelegate addFunc = nullptr;
	if (CreateManagedDelegate(TestAppDLLName, TestClassName, "Add", (void**)(&addFunc)) && addFunc)
	{
		std::wcout << L"[Example 2] Calling Add(10, 32):" << std::endl;
		int sum = addFunc(10, 32);
		std::wcout << L"Result: " << sum << std::endl << std::endl;
	}

	// Example 3: Call LogMessage (void return)
	LogMessageDelegate logFunc = nullptr;
	if (CreateManagedDelegate(TestAppDLLName, TestClassName, "LogMessage", (void**)(&logFunc)) && logFunc)
	{
		std::wcout << L"[Example 3] Calling LogMessage():" << std::endl;
		logFunc("This is a message from C++ to C#");
		std::wcout << std::endl;
	}
}

void TestEntitySystem(CreateManagedDelegateFunc CreateManagedDelegate)
{
	std::wcout << L"\n========== Entity Component System Test ==========" << std::endl;

	// Create entity manager
	criollo::EntityManager entityManager;
	entityManager.Initialize();

	// Create entities
	criollo::Entity* player = entityManager.CreateEntity("Player");
	player->transform.position = { 0.0f, 0.0f, 0.0f };

	const char* TestAppDLLName = "TestScript";
	const char* EntityBridgeClassName = "TestScript.Scene.EntityBridge";
	const char* InternalCallsClassName = "TestScript.Scene.InternalCalls";

	// Initialize internal call delegates (C++ -> C# property setters)
	std::wcout << L"Initializing internal call system..." << std::endl;
	
	//  Create C++ function wrappers that will be called from C#
	typedef void (CORECLR_DELEGATE_CALLTYPE *SetGetTransformDelegateFunc)(Entity_GetTransformDelegate);
	typedef void (CORECLR_DELEGATE_CALLTYPE *SetSetTransformDelegateFunc)(Entity_SetTransformDelegate);
	
	SetGetTransformDelegateFunc setGetTransformDelegate = nullptr;
	SetSetTransformDelegateFunc setSetTransformDelegate = nullptr;
	
	// Create delegates to set the C# delegate properties
	if (!CreateManagedDelegate(TestAppDLLName, InternalCallsClassName, "set_Entity_GetTransform", (void**)(&setGetTransformDelegate)))
	{
		std::wcerr << L"Failed to create set_Entity_GetTransform delegate" << std::endl;
	}
	else
	{
		// Create our C++ implementation delegate
		Entity_GetTransformDelegate getTransformImpl = [](uint64_t entityID, criollo::TransformComponent* outTransform) {
			criollo::ScriptBindings::Entity_GetTransform(entityID, outTransform);
		};
		setGetTransformDelegate(getTransformImpl);
		std::wcout << L"Entity_GetTransform initialized!" << std::endl;
	}
	
	if (!CreateManagedDelegate(TestAppDLLName, InternalCallsClassName, "set_Entity_SetTransform", (void**)(&setSetTransformDelegate)))
	{
		std::wcerr << L"Failed to create set_Entity_SetTransform delegate" << std::endl;
	}
	else
	{
		// Create our C++ implementation delegate
		Entity_SetTransformDelegate setTransformImpl = [](uint64_t entityID, criollo::TransformComponent* transform) {
			criollo::ScriptBindings::Entity_SetTransform(entityID, transform);
		};
		setSetTransformDelegate(setTransformImpl);
		std::wcout << L"Entity_SetTransform initialized!" << std::endl;
	}

	// Create delegates for lifecycle methods (using static bridge methods)
	EntityStartDelegate startDelegate = nullptr;
	EntityUpdateDelegate updateDelegate = nullptr;
	EntityStopDelegate stopDelegate = nullptr;

	if (!CreateManagedDelegate(TestAppDLLName, EntityBridgeClassName, "Start", (void**)(&startDelegate)))
	{
		std::wcerr << L"Failed to create Start delegate" << std::endl;
		entityManager.Shutdown();
		return;
	}

	if (!CreateManagedDelegate(TestAppDLLName, EntityBridgeClassName, "Update", (void**)(&updateDelegate)))
	{
		std::wcerr << L"Failed to create Update delegate" << std::endl;
		entityManager.Shutdown();
		return;
	}

	if (!CreateManagedDelegate(TestAppDLLName, EntityBridgeClassName, "Stop", (void**)(&stopDelegate)))
	{
		std::wcerr << L"Failed to create Stop delegate" << std::endl;
		entityManager.Shutdown();
		return;
	}

	std::wcout << L"Successfully created all entity lifecycle delegates!" << std::endl;

	// Create entity instance in C# (using EntityBridge)
	typedef void (CORECLR_DELEGATE_CALLTYPE *CreateEntityInstanceDelegate)(uint64_t entityID, const char* typeName);
	CreateEntityInstanceDelegate createInstanceDelegate = nullptr;
	
	if (CreateManagedDelegate(TestAppDLLName, EntityBridgeClassName, "CreateEntityInstance", (void**)(&createInstanceDelegate)))
	{
		std::wcout << L"Calling CreateEntityInstance with ID=" << player->id << L", Type=TestScript.Scene.PlayerController" << std::endl;
		createInstanceDelegate(player->id, "TestScript.Scene.PlayerController");
		std::wcout << L"CreateEntityInstance completed" << std::endl;
	}
	else
	{
		std::wcerr << L"Failed to create CreateEntityInstance delegate" << std::endl;
		entityManager.Shutdown();
		return;
	}

	// Add a small delay to let C# logging flush
	std::this_thread::sleep_for(std::chrono::milliseconds(100));

	// Attach script to entity
	entityManager.AttachScript(player->id, "TestScript.Scene.PlayerController", nullptr);
	
	// Set the delegates for the script
	entityManager.SetScriptDelegates(player->id, startDelegate, updateDelegate, stopDelegate);

	std::wcout << L"About to call StartEntity..." << std::endl;

	// Start the entity
	entityManager.StartEntity(player->id);

	std::wcout << L"StartEntity completed!" << std::endl;

	// Simulate game loop
	std::wcout << L"\n--- Simulating game loop for 3 seconds ---" << std::endl;
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
			std::wcout << L"Frame " << frameCount << L": Position(" 
					   << pos.x << L", " << pos.y << L", " << pos.z << L")" << std::endl;
		}

		frameCount++;
		
		// Sleep to simulate frame timing (optional, for readability)
		std::this_thread::sleep_for(std::chrono::milliseconds(16));
	}

	auto endTime = std::chrono::high_resolution_clock::now();
	auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime).count();
	
	std::wcout << L"\n--- Game loop finished ---" << std::endl;
	std::wcout << L"Total frames: " << frameCount << std::endl;
	std::wcout << L"Duration: " << duration << L"ms" << std::endl;

	// Stop the entity
	entityManager.StopEntity(player->id);

	// Unregister entity from C#
	typedef void (CORECLR_DELEGATE_CALLTYPE *UnregisterEntityDelegate)(uint64_t entityID);
	UnregisterEntityDelegate unregisterDelegate = nullptr;
	if (CreateManagedDelegate(TestAppDLLName, EntityBridgeClassName, "UnregisterEntity", (void**)(&unregisterDelegate)))
	{
		unregisterDelegate(player->id);
	}

	// IMPORTANT: Clear C# delegate references before shutdown
	std::wcout << L"Clearing C# delegate references..." << std::endl;
	
	// Clear EntityBridge dictionary
	typedef void (CORECLR_DELEGATE_CALLTYPE *ClearEntityBridgeDelegate)();
	ClearEntityBridgeDelegate clearEntityBridge = nullptr;
	if (CreateManagedDelegate(TestAppDLLName, EntityBridgeClassName, "ClearAll", (void**)(&clearEntityBridge)))
	{
		clearEntityBridge();
		std::wcout << L"EntityBridge cleared" << std::endl;
	}
	
	// Clear internal call delegates to prevent dangling pointers
	typedef void (CORECLR_DELEGATE_CALLTYPE *ClearInternalCallsDelegate)();
	ClearInternalCallsDelegate clearInternalCalls = nullptr;
	if (CreateManagedDelegate(TestAppDLLName, InternalCallsClassName, "ClearDelegates", (void**)(&clearInternalCalls)))
	{
		clearInternalCalls();
		std::wcout << L"Internal call delegates cleared" << std::endl;
	}

	// Cleanup C++ side
	entityManager.Shutdown();
	
	std::wcout << L"Entity system shutdown complete" << std::endl;
}

int main()
{
	std::wcout << L"Criollo CoreCLR Host Test" << std::endl;

	// Load the Criollo DLL
	HMODULE hCoreDll = LoadLibraryW(L"Criollo.dll");
	if (!hCoreDll)
	{
		std::wcerr << L"Failed to load Criollo.dll" << std::endl;
		return 1;
	}

	// Get function pointers
	auto InitializeCoreRuntime = (InitializeCoreRuntimeFunc)(GetProcAddress(hCoreDll, "InitializeCoreRuntime"));
	auto ShutdownCoreRuntime = (ShutdownCoreRuntimeFunc)(GetProcAddress(hCoreDll, "ShutdownCoreRuntime"));
	auto ExecuteManagedAssembly = (ExecuteManagedAssemblyFunc)(GetProcAddress(hCoreDll, "ExecuteManagedAssembly"));
	auto CreateManagedDelegate = (CreateManagedDelegateFunc)(GetProcAddress(hCoreDll, "CreateManagedDelegate"));

	if (!InitializeCoreRuntime || !ShutdownCoreRuntime)
	{
		std::wcerr << L"Failed to get function pointers from Criollo.dll" << std::endl;
		FreeLibrary(hCoreDll);
		return 1;
	}

	std::wstring runtimePath = LR"(C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\10.0.0)";
	std::wstring assemblyPath = std::filesystem::current_path().wstring() + L"\\TestScript.dll";

	// Check if paths exist
	bool pathsExist = true;
	if (!std::filesystem::exists(runtimePath))
	{
		std::wcerr << L"Warning: Runtime path does not exist: " << runtimePath << std::endl;
		std::wcerr << L"Please update the runtimePath variable with your .NET installation path." << std::endl;
		pathsExist = false;
	}

	if (!std::filesystem::exists(assemblyPath))
	{
		std::wcerr << L"Warning: Assembly path does not exist: " << assemblyPath << std::endl;
		std::wcerr << L"Please build TestScript project or update the assemblyPath variable." << std::endl;
		pathsExist = false;
	}

	if (!pathsExist)
	{
		std::wcout << std::endl << L"Press Enter to exit...";
		std::wcin.get();
		FreeLibrary(hCoreDll);
		return 1;
	}

	// Initialize CoreCLR
	std::wcout << L"Initializing CoreCLR..." << std::endl;
	if (!InitializeCoreRuntime(runtimePath.c_str(), assemblyPath.c_str()))
	{
		std::wcerr << L"Failed to initialize CoreCLR runtime" << std::endl;
		std::wcerr << L"Make sure the runtime path points to a valid .NET installation." << std::endl;
		FreeLibrary(hCoreDll);
		return 1;
	}
	std::wcout << L"CoreCLR initialized successfully!" << std::endl << std::endl;

	// Test basic functionality
	TestBasicFunctionality(CreateManagedDelegate);

	// Test Entity Component System
	TestEntitySystem(CreateManagedDelegate);

	// Shutdown
	std::wcout << std::endl;
	std::wcout << L"Shutting down..." << std::endl;
	
	// Give C# GC a chance to finalize objects
	std::wcout << L"Waiting for cleanup..." << std::endl;
	std::this_thread::sleep_for(std::chrono::milliseconds(100));
	
	// Shutdown CoreCLR
	std::wcout << L"Shutting down CoreCLR..." << std::endl;
	ShutdownCoreRuntime();
	std::wcout << L"CoreCLR shutdown complete" << std::endl;
	
	// Unload DLL
	std::wcout << L"Unloading Criollo.dll..." << std::endl;
	FreeLibrary(hCoreDll);
	std::wcout << L"Criollo.dll unloaded" << std::endl;

	std::wcout << L"Application terminated successfully" << std::endl;
	return 0;
}