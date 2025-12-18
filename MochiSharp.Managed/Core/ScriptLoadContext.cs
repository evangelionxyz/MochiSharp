using System;
using System.Reflection;
using System.Runtime.Loader;

namespace MochiSharp.Managed.Core;

/// <summary>
/// Custom AssemblyLoadContext for loading script assemblies in isolation.
/// This allows unloading and reloading of script assemblies without affecting the core engine.
/// </summary>
public class ScriptLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public ScriptLoadContext(string assemblyPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Allow MochiSharp.Managed to be shared between contexts
        if (assemblyName.Name == "MochiSharp.Managed")
        {
            return null; // Use default context's version
        }

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}

/// <summary>
/// Manages loading and unloading of script assemblies using AssemblyLoadContext.
/// </summary>
public class ScriptAssemblyManager
{
    private ScriptLoadContext? _loadContext;
    private Assembly? _scriptAssembly;

    /// <summary>
    /// Loads a script assembly into an isolated context.
    /// </summary>
    /// <param name="assemblyPath">Full path to the script assembly DLL</param>
    /// <returns>The loaded assembly, or null if loading failed</returns>
    public Assembly? LoadScriptAssembly(string assemblyPath)
    {
        try
        {
            Debug.Log($"[ScriptAssemblyManager] Loading script assembly from: {assemblyPath}");

            // Create a new load context for this assembly
            _loadContext = new ScriptLoadContext(assemblyPath);
            _scriptAssembly = _loadContext.LoadFromAssemblyPath(assemblyPath);

            Debug.Log($"[ScriptAssemblyManager] Successfully loaded: {_scriptAssembly.FullName}");
            return _scriptAssembly;
        }
        catch (Exception ex)
        {
            Debug.Log($"[ScriptAssemblyManager] Failed to load script assembly: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Unloads the currently loaded script assembly and its context.
    /// </summary>
    public void UnloadScriptAssembly()
    {
        if (_loadContext != null)
        {
            Debug.Log("[ScriptAssemblyManager] Unloading script assembly context...");
            
            _scriptAssembly = null;
            _loadContext.Unload();
            _loadContext = null;

            // Force garbage collection to ensure context is cleaned up
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            Debug.Log("[ScriptAssemblyManager] Script assembly context unloaded");
        }
    }

    /// <summary>
    /// Gets the currently loaded script assembly.
    /// </summary>
    public Assembly? ScriptAssembly => _scriptAssembly;

    /// <summary>
    /// Gets whether a script assembly is currently loaded.
    /// </summary>
    public bool IsLoaded => _scriptAssembly != null && _loadContext != null;
}
