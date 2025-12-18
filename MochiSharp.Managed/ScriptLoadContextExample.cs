using System;
using MochiSharp.Managed.Core;

namespace MochiSharp.Managed;

/// <summary>
/// Example demonstrating how to use ScriptLoadContext to load script assemblies in isolation.
/// This allows MochiSharp.Managed (core engine) and TestScript (game scripts) to be loaded separately.
/// </summary>
public class ScriptLoadContextExample
{
    public static void Example()
    {
        // Create the script assembly manager
        var scriptManager = new ScriptAssemblyManager();

        // Load the TestScript assembly in an isolated context
        string scriptPath = @"C:\Dev\MochiSharp\bin\Debug\Any CPU\TestScript.dll";
        var scriptAssembly = scriptManager.LoadScriptAssembly(scriptPath);

        if (scriptAssembly != null)
        {
            Debug.Log($"Loaded script assembly: {scriptAssembly.FullName}");
            
            // Use the assembly...
            // For example, create entity instances from types in this assembly
            
            // When done, unload the script assembly
            scriptManager.UnloadScriptAssembly();
        }
    }
}
