#include <iostream>
#include <windows.h>
#include <filesystem>

namespace fs = std::filesystem;

// Function pointers for CriolloCore DLL
typedef bool (*InitializeCoreRuntimeFunc)(const wchar_t*, const wchar_t*);
typedef void (*ShutdownCoreRuntimeFunc)();
typedef bool (*ExecuteManagedAssemblyFunc)(const wchar_t*);
typedef bool (*CreateTestMethodDelegateFunc)(void**);
typedef bool (*CreateManagedDelegateFunc)(const char*, const char*, const char*, void**);

typedef int (*TestMethodDelegate)();
typedef int (*TestMethodDelegate)();
typedef int (*AddDelegate)(int, int);
typedef const char* (*GreetDelegate)(const char*);
typedef void (*LogMessageDelegate)(const char*);

int main()
{
	std::wcout << L"Criollo CoreCLR Host Test" << std::endl;

	// Load the CriolloCore DLL
	HMODULE hCoreDll = LoadLibraryW(L"CriolloCore.dll");
	if (!hCoreDll)
	{
		std::wcerr << L"Failed to load CriolloCore.dll" << std::endl;
		return 1;
	}

	// Get function pointers
	auto InitializeCoreRuntime = (InitializeCoreRuntimeFunc)(GetProcAddress(hCoreDll, "InitializeCoreRuntime"));
	auto ShutdownCoreRuntime = (ShutdownCoreRuntimeFunc)(GetProcAddress(hCoreDll, "ShutdownCoreRuntime"));
	auto ExecuteManagedAssembly = (ExecuteManagedAssemblyFunc)(GetProcAddress(hCoreDll, "ExecuteManagedAssembly"));
	auto CreateManagedDelegate = (CreateManagedDelegateFunc)(GetProcAddress(hCoreDll, "CreateManagedDelegate"));

	if (!InitializeCoreRuntime || !ShutdownCoreRuntime)
	{
		std::wcerr << L"Failed to get function pointers from CriolloCore.dll" << std::endl;
		FreeLibrary(hCoreDll);
		return 1;
	}

	std::wstring runtimePath = LR"(C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\10.0.0)";
	std::wstring assemblyPath = std::filesystem::current_path().wstring() + L"\\TestScript.dll";

	// Check if paths exist
	bool pathsExist = true;
	if (!fs::exists(runtimePath))
	{
		std::wcerr << L"Warning: Runtime path does not exist: " << runtimePath << std::endl;
		std::wcerr << L"Please update the runtimePath variable with your .NET installation path." << std::endl;
		pathsExist = false;
	}

	if (!fs::exists(assemblyPath))
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

	// Create a delegate to the TestMethod
	std::wcout << L"Creating delegate to Criollo.Test.TestMethod()..." << std::endl;
	
	// Example 1: Call TestMethod
	TestMethodDelegate testMethod = nullptr;
	if (CreateManagedDelegate("TestScript", "Criollo.Test", "TestMethod", reinterpret_cast<void**>(&testMethod)) && testMethod)
	{
		std::wcout << L"[Example 1] Calling TestMethod():" << std::endl;
		int result = testMethod();
		std::wcout << L"Result: " << result << std::endl << std::endl;
	}

	// Example 2: Call Add method with parameters
	AddDelegate addFunc = nullptr;
	if (CreateManagedDelegate("TestScript", "Criollo.Test", "Add", reinterpret_cast<void**>(&addFunc)) && addFunc)
	{
		std::wcout << L"[Example 2] Calling Add(10, 32):" << std::endl;
		int sum = addFunc(10, 32);
		std::wcout << L"Result: " << sum << std::endl << std::endl;
	}

	// Example 3: Call LogMessage (void return)
	LogMessageDelegate logFunc = nullptr;
	if (CreateManagedDelegate("TestScript", "Criollo.Test", "LogMessage", reinterpret_cast<void**>(&logFunc)) && logFunc)
	{
		std::wcout << L"[Example 3] Calling LogMessage():" << std::endl;
		logFunc("This is a message from C++ to C#");
		std::wcout << std::endl;
	}

	// Shutdown
	std::wcout << std::endl;
	std::wcout << L"Shutting down..." << std::endl;
	ShutdownCoreRuntime();
	FreeLibrary(hCoreDll);

	std::wcout << std::endl << L"Press Enter to exit...";
	std::wcin.get();
	return 0;
}