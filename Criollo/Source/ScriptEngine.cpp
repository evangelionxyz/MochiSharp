// Copyright (c) 2025 Evangelion Manuhutu

#include "pch.h"
#include "ScriptEngine.h"

namespace criollo
{
	ScriptEngine::ScriptEngine()
		: m_hCoreDll(nullptr)
		, m_pfnInitialize(nullptr)
		, m_pfnShutdown(nullptr)
		, m_pfnCreateDelegate(nullptr)
		, m_isInitialized(false)
	{
	}

	ScriptEngine::~ScriptEngine()
	{
		Shutdown();
	}

	bool ScriptEngine::LoadCoreLibrary(const std::wstring& dllPath)
	{
		m_hCoreDll = LoadLibraryW(dllPath.c_str());
		if (!m_hCoreDll)
		{
			return false;
		}

		m_pfnInitialize = reinterpret_cast<PFN_InitializeCoreRuntime>(GetProcAddress(m_hCoreDll, "InitializeCoreRuntime"));
		m_pfnShutdown = reinterpret_cast<PFN_ShutdownCoreRuntime>(GetProcAddress(m_hCoreDll, "ShutdownCoreRuntime"));
		m_pfnCreateDelegate = reinterpret_cast<PFN_CreateManagedDelegate>(GetProcAddress(m_hCoreDll, "CreateManagedDelegate"));
		return m_pfnInitialize && m_pfnShutdown && m_pfnCreateDelegate;
	}

	bool ScriptEngine::Initialize(const std::wstring& runtimePath, const std::wstring& assemblyPath)
	{
		if (!m_pfnInitialize)
		{
			return false;
		}

		m_isInitialized = m_pfnInitialize(runtimePath.c_str(), assemblyPath.c_str());
		return m_isInitialized;
	}

	void ScriptEngine::Shutdown()
	{
		if (m_pfnShutdown && m_isInitialized)
		{
			m_pfnShutdown();
			m_isInitialized = false;
		}

		if (m_hCoreDll)
		{
			FreeLibrary(m_hCoreDll);
			m_hCoreDll = nullptr;
		}

		m_pfnInitialize = nullptr;
		m_pfnShutdown = nullptr;
		m_pfnCreateDelegate = nullptr;
	}
}
