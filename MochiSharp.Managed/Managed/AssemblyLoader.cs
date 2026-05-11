using MochiSharp.Managed.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MochiSharp.Managed;

public enum AssemblyLoadStatus
{
    Success, FileNotFound, FileLoadFailure, InvalidFilePath, InvalidAssembly, UnknownError
}

public static class AssemblyLoader
{
    public static readonly Dictionary<int, AssemblyLoadContext?> s_AssemblyContexts = new();
    private static Dictionary<int, string[]> s_AlcDllPaths = new();
    private static readonly Dictionary<Type, AssemblyLoadStatus> s_AssemblyLoadErrorLookup = new();
    private static readonly Dictionary<int, Dictionary<int, Assembly>> s_AssemblyCache = new();
#if DEBUG
    private static readonly Dictionary<int, List<GCHandle>> s_AllocatedHandles = new();
#endif
    private static readonly AssemblyLoadContext? s_AssemblyLoadContext;
    private static readonly int MOCHI_ALC_CACHE_ID = -1;
    private static AssemblyLoadStatus s_LastLoadStatus = AssemblyLoadStatus.Success;

    static AssemblyLoader()
    {
        s_AssemblyLoadErrorLookup.Add(typeof(BadImageFormatException), AssemblyLoadStatus.InvalidAssembly);
        s_AssemblyLoadErrorLookup.Add(typeof(FileNotFoundException), AssemblyLoadStatus.FileNotFound);
        s_AssemblyLoadErrorLookup.Add(typeof(FileLoadException), AssemblyLoadStatus.FileLoadFailure);
        s_AssemblyLoadErrorLookup.Add(typeof(ArgumentNullException), AssemblyLoadStatus.InvalidFilePath);
        s_AssemblyLoadErrorLookup.Add(typeof(ArgumentException), AssemblyLoadStatus.InvalidFilePath);

        s_AssemblyLoadContext = AssemblyLoadContext.GetLoadContext(typeof(AssemblyLoader).Assembly);
        s_AssemblyLoadContext!.Resolving += ResolveAssembly;

        s_AssemblyCache.Add(MOCHI_ALC_CACHE_ID, new());
    }

    private static void CacheAssemblies()
    {
        foreach (var assembly in s_AssemblyLoadContext!.Assemblies)
        {
            int assemblyId = assembly.GetName().Name!.GetHashCode();
            s_AssemblyCache[MOCHI_ALC_CACHE_ID].Add(assemblyId, assembly);
        }
    }

    internal static bool TryGetAssembly(int assemblyLoadContextId, int assemblyId, out Assembly? outAssembly)
    {
        return s_AssemblyCache[assemblyLoadContextId].TryGetValue(assemblyId, out outAssembly);
    }

    internal static Assembly? ResolveAssembly(AssemblyLoadContext? assemblyLoadContext, AssemblyName assemblyName)
    {
        try
        {
            if (assemblyName.Name == null)
            {
                throw new ArgumentNullException("assemblyName");
            }

            int assemblyId = assemblyName.Name.GetHashCode();
            if (assemblyLoadContext == null || assemblyLoadContext.Name == null)
            {
                // Search all the assemblies
                foreach (var cache in s_AssemblyCache)
                {
                    foreach (KeyValuePair<int, Assembly> entry in cache.Value)
                    {
                        if (assemblyName.Name == entry.Value.GetName().Name)
                        {
                            return entry.Value;
                        }
                    }
                }

                return null;
            }

            int alcId = assemblyLoadContext.Name.GetHashCode();
            if (s_AssemblyCache[alcId].TryGetValue(assemblyId, out var cachedAssembly))
            {
                return cachedAssembly;
            }

            if (s_AssemblyLoadContext != null)
            {
                foreach (var assembly in s_AssemblyLoadContext.Assemblies)
                {
                    if (assembly.GetName().Name != assemblyName.Name)
                        continue;

                    return assembly;
                }
            }

            foreach (var assembly in assemblyLoadContext.Assemblies)
            {
                if (assembly.GetName().Name != assemblyName.Name)
                    continue;

                s_AssemblyCache[alcId].Add(assemblyId, assembly);
                return assembly;
            }
        }
        catch (Exception ex)
        {
            ManagedHost.HandleException(ex);
        }

        Assembly? resolved;
        var tryResolve = (string directory) =>
        {
            string assemblyPath = Path.Combine(directory, $"{assemblyName.Name}.dll");
            ManagedHost.LogMessage($"[AssemblyLoader] Trying to find assembly in {assemblyPath}", MessageLevel.Trace);
            if (assemblyLoadContext != null && File.Exists(assemblyPath))
            {
                ManagedHost.LogMessage($"[AssemblyLoader] Found assembly {assemblyName.FullName}", MessageLevel.Trace);
                return assemblyLoadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            }

            return null;
        };

        if ((resolved = tryResolve(AppContext.BaseDirectory)) != null)
            return resolved;

        if (assemblyLoadContext != null && assemblyLoadContext.Name != null)
        {
            int alcId = assemblyLoadContext.Name.GetHashCode();
            foreach (var path in s_AlcDllPaths[alcId])
            {
                if ((resolved = tryResolve(path)) != null)
                    return resolved;
            }
        }

        return null;
    }

    [UnmanagedCallersOnly]
    internal static int CreateAssemblyLoadContext(NativeString inName, NativeString dllPath)
    {
        string? name = inName;

        if (name == null)
            return -1;

        var alc = new AssemblyLoadContext(name, true);
        alc.Resolving += ResolveAssembly;
        alc.Unloading += ctx => s_AssemblyCache.Remove(ctx.Name!.GetHashCode());

        int contextId = name.GetHashCode();
        s_AssemblyContexts.Add(contextId, alc);
        s_AssemblyCache.Add(contextId, new());

        var path = dllPath.ToString();
        ManagedHost.LogMessage($"Added ALC '{name}' with ID '{contextId}'", MessageLevel.Trace);
        s_AlcDllPaths.Add(contextId, (path ?? "").Split(":"));

        return contextId;
    }

    [UnmanagedCallersOnly]
    internal static void UnloadAssemblyLoadContext(int contextId)
    {
        if (!s_AssemblyContexts.TryGetValue(contextId, out var alc))
        {
            ManagedHost.LogMessage($"Cannot unload AssemblyLoadContext '{contextId}', it was either never loaded or already unloaded.", MessageLevel.Trace);
            return;
        }

        if (alc == null)
        {
            ManagedHost.LogMessage($"AssemblyLoadContext '{contextId}' was found in dictionary but was null. This is most likely a bug.", MessageLevel.Error);
            return;
        }

#if DEBUG
        foreach (var assembly in alc.Assemblies)
        {
            var assemblyName = assembly.GetName();
            int assemblyId = assemblyName.Name!.GetHashCode();

            if (!s_AllocatedHandles.TryGetValue(assemblyId, out var handles))
            {
                continue;
            }

            // If everything is working properly, then there should not be anything left kicking around in the handles list.
            // If you see messages here, it probably means you are mis-managing the lifetime of unmanaged resources.
            // Managed objects that wrap an unmanaged resource need to implement IDisposable, and be Dispose()'d properly.
            // Example:
            //    // SceneQueryHitInterop wraps an unmanaged resource. It needs to implement IDisposable
            //    using(SceneQueryHitInterop hit = new())
            //    {
            //        Physics.CastRay(ray, out hit);   // Calls into native code, populates the unmanaged resource into hit
            //
            //        // Do something with hit
            //
            //    } // hit is Dispose()'d here
            //
            foreach (var handle in handles)
            {
                ManagedHost.LogMessage($"Found still-registered handle '{(handle.Target is null ? "null" : handle.Target)}' from assembly '{assemblyName}'", MessageLevel.Warning);

                if (!handle.IsAllocated || handle.Target == null)
                {
                    continue;
                }

                ManagedHost.LogMessage($"Found unfreed object '{handle.Target}' from assembly '{assemblyName}'. Deallocating.", MessageLevel.Warning);
                handle.Free();
            }

            s_AllocatedHandles.Remove(assemblyId);
        }
#endif

        ManagedObject.s_CachedMethods.Clear();

        TypeInterface.s_CachedTypes.Clear();
        TypeInterface.s_CachedMethods.Clear();
        TypeInterface.s_CachedFields.Clear();
        TypeInterface.s_CachedProperties.Clear();
        TypeInterface.s_CachedAttributes.Clear();

        s_AssemblyContexts.Remove(contextId);
        s_AlcDllPaths.Remove(contextId);
        alc.Unload();
    }

    [UnmanagedCallersOnly]
    internal static int LoadAssembly(int contextId, NativeString assemblyFilepath)
    {
        try
        {
            if (string.IsNullOrEmpty(assemblyFilepath))
            {
                s_LastLoadStatus = AssemblyLoadStatus.InvalidFilePath;
                return -1;
            }

            if (!File.Exists(assemblyFilepath))
            {
                ManagedHost.LogMessage($"Failed to load assembly '{assemblyFilepath}', file not found.", MessageLevel.Error);
                s_LastLoadStatus = AssemblyLoadStatus.FileNotFound;
                return -1;
            }

            if (!s_AssemblyContexts.TryGetValue(contextId, out var alc))
            {
                ManagedHost.LogMessage($"Failed to load assembly '{assemblyFilepath}', couldn't find AssemblyLoadContext with id {contextId}.", MessageLevel.Error);
                s_LastLoadStatus = AssemblyLoadStatus.UnknownError;
                return -1;
            }

            if (alc == null)
            {
                ManagedHost.LogMessage($"Failed to load assembly '{assemblyFilepath}', AssemblyLoadContext with id {contextId} was null.", MessageLevel.Error);
                s_LastLoadStatus = AssemblyLoadStatus.UnknownError;
                return -1;
            }

            Assembly? assembly = null;

            using (var file = MemoryMappedFile.CreateFromFile(assemblyFilepath!))
            {
                using var stream = file.CreateViewStream();
                assembly = alc.LoadFromStream(stream);
            }

            ManagedHost.LogMessage($"Loading assembly '{assemblyFilepath}'", MessageLevel.Info);
            var assemblyName = assembly.GetName();
            int assemblyId = assemblyName.Name!.GetHashCode();
            s_AssemblyCache[contextId].Add(assemblyId, assembly);
            s_LastLoadStatus = AssemblyLoadStatus.Success;
            return assemblyId;
        }
        catch (Exception ex)
        {
            s_AssemblyLoadErrorLookup.TryGetValue(ex.GetType(), out s_LastLoadStatus);
            ManagedHost.HandleException(ex);
            return -1;
        }
    }

    [UnmanagedCallersOnly]
    internal static unsafe int LoadAssemblyFromMemory(int InContextId, byte* data, long dataLength)
    {
        try
        {
            if (!s_AssemblyContexts.TryGetValue(InContextId, out var alc))
            {
                ManagedHost.LogMessage($"Failed to load assembly, couldn't find AssemblyLoadContext with id {InContextId}.", MessageLevel.Error);
                s_LastLoadStatus = AssemblyLoadStatus.UnknownError;
                return -1;
            }

            if (alc == null)
            {
                ManagedHost.LogMessage($"Failed to load assembly, couldn't find AssemblyLoadContext with id {InContextId} was null.", MessageLevel.Error);
                s_LastLoadStatus = AssemblyLoadStatus.UnknownError;
                return -1;
            }

            Assembly? assembly = null;

            using (var stream = new UnmanagedMemoryStream(data, dataLength))
            {
                assembly = alc.LoadFromStream(stream);
            }

            ManagedHost.LogMessage($"Loading assembly '{assembly.FullName}'", MessageLevel.Info);
            var assemblyName = assembly.GetName();
            int assemblyId = assemblyName.Name!.GetHashCode();
            s_AssemblyCache[InContextId].Add(assemblyId, assembly);
            s_LastLoadStatus = AssemblyLoadStatus.Success;
            return assemblyId;
        }
        catch (Exception ex)
        {
            s_AssemblyLoadErrorLookup.TryGetValue(ex.GetType(), out s_LastLoadStatus);
            ManagedHost.HandleException(ex);
            return -1;
        }
    }

    [UnmanagedCallersOnly]
    internal static AssemblyLoadStatus GetLastLoadStatus() => s_LastLoadStatus;

    [UnmanagedCallersOnly]
    internal static NativeString GetAssemblyName(int InContextId, int InAssemblyId)
    {
        if (!s_AssemblyCache[InContextId].TryGetValue(InAssemblyId, out var assembly))
        {
            ManagedHost.LogMessage($"Couldn't get assembly name for assembly '{InAssemblyId}', assembly not in dictionary.", MessageLevel.Error);
            return "";
        }

        var assemblyName = assembly.GetName();
        return assemblyName.Name;
    }

#if DEBUG
    // In DEBUG builds, we track all GCHandles that are allocated by the managed code,
    // so that we can check that they've all been freed when the assembly is unloaded.
    internal static void RegisterHandle(Assembly InAssembly, GCHandle InHandle)
    {
        var assemblyName = InAssembly.GetName();
        int assemblyId = assemblyName.Name!.GetHashCode();

        if (!s_AllocatedHandles.TryGetValue(assemblyId, out var handles))
        {
            s_AllocatedHandles.Add(assemblyId, new List<GCHandle>());
            handles = s_AllocatedHandles[assemblyId];
        }

        handles.Add(InHandle);
    }

    internal static void DeregisterHandle(Assembly InAssembly, GCHandle InHandle)
    {
        var assemblyName = InAssembly.GetName();
        int assemblyId = assemblyName.Name!.GetHashCode();

        if (!s_AllocatedHandles.TryGetValue(assemblyId, out var handles))
        {
            return;
        }

        if (!InHandle.IsAllocated)
        {
            ManagedHost.LogMessage($"AssemblyLoader de-registering an already freed object from assembly '{assemblyName}'", MessageLevel.Error);
        }

        handles.Remove(InHandle);
    }
#endif
}
