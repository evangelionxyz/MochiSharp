using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MochiSharp.Managed.Core
{
    public static class Bootstrap
    {
        private static HostHook? _hostHook;
        private static ScriptContext? _scriptContext;

        private static int LoadAssemblyCore(string path)
        {
            if (_scriptContext != null)
            {
                _scriptContext.Unload();
                _scriptContext = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                _scriptContext = new ScriptContext(fullPath);
                _hostHook?.Log($"Loaded Script Assembly: {fullPath}");
                return 1;
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"Failed to load script assembly: {ex}");
                return 0;
            }
        }

        private static string GetDerivedTypesCore(string asmPath, string baseTypeFullName)
        {
            try
            {
                string fullPath = Path.GetFullPath(asmPath);
                if (_scriptContext != null && string.Equals(_scriptContext.PluginPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return _scriptContext.GetDerivedTypes(baseTypeFullName);
                }

                var asm = Assembly.LoadFrom(asmPath);

                string asmDir = Path.GetDirectoryName(asm.Location) ?? string.Empty;
                foreach (var reference in asm.GetReferencedAssemblies())
                {
                    _ = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, reference.Name, StringComparison.OrdinalIgnoreCase))
                        ?? TryLoadReference(reference, asmDir);
                }

                var baseType = Type.GetType(baseTypeFullName, throwOnError: false)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType(baseTypeFullName, throwOnError: false))
                        .FirstOrDefault(t => t != null)
                    ?? throw new Exception($"Base type not found: {baseTypeFullName}");

                var derived = asm.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
                    .Select(t => t.FullName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToArray();

                return string.Join("|", derived);
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"GetDerivedTypes failed for {asmPath}: {ex.Message}");
                return string.Empty;
            }
        }

        private static Assembly? TryLoadReference(AssemblyName reference, string asmDir)
        {
            try
            {
                return Assembly.Load(reference);
            }
            catch
            {
                try
                {
                    string candidate = Path.Combine(asmDir, $"{reference.Name}.dll");
                    if (File.Exists(candidate))
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        [UnmanagedCallersOnly]
        public static IntPtr GetDerivedTypes(IntPtr asmPathPtr, IntPtr baseTypeFullNamePtr)
        {
            string asmPath = Marshal.PtrToStringUTF8(asmPathPtr) ?? string.Empty;
            string baseTypeFullName = Marshal.PtrToStringUTF8(baseTypeFullNamePtr) ?? string.Empty;

            string result = GetDerivedTypesCore(asmPath, baseTypeFullName);
            return Marshal.StringToCoTaskMemUTF8(result);
        }

        private static ScriptContext GetContextOrThrow()
        {
            if (_scriptContext == null)
            {
                throw new InvalidOperationException("No ScriptContext loaded. Call LoadAssembly first.");
            }

            return _scriptContext;
        }

        // Structure to hold C++ function pointers (Engine API)
        [StructLayout(LayoutKind.Sequential)]
        public struct EngineInterface
        {
            public IntPtr LogMessage;
        }

        // Entry point called by C++
        // UnmanagedCallersOnly is crucial for preformant revers-P/Invoke
        [UnmanagedCallersOnly]
        public static int Initialize(IntPtr engineArgs)
        {
            var engineApi = Marshal.PtrToStructure<EngineInterface>(engineArgs);

            _hostHook = new HostHook(engineApi);
            _hostHook.Log("C# Managed Core Initialized successfully");

            return 0;
        }

        // Load/Reload a plugin assembly into a collectible context.
        [UnmanagedCallersOnly]
        public static int LoadAssembly(IntPtr assemblyPathPtr)
        {
            string path = Marshal.PtrToStringUTF8(assemblyPathPtr)!;
            return LoadAssemblyCore(path);
        }

        // Create a script instance; returns a positive handle, 0 on error.
        [UnmanagedCallersOnly]
        public static int CreateInstance(IntPtr typeNamePtr)
        {
            try
            {
                string typeName = Marshal.PtrToStringUTF8(typeNamePtr)!;
                int id = GetContextOrThrow().CreateInstance(typeName);
                _hostHook?.Log($"Created instance {id}: {typeName}");
                return id;
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"CreateInstance failed: {ex}");
                return 0;
            }
        }

        // Create a script instance with a caller-supplied instance key.
        // Returns 1 on success, 0 on error.
        [UnmanagedCallersOnly]
        public static int CreateInstanceGuid(IntPtr typeNamePtr, IntPtr instanceGuidPtr)
        {
            try
            {
                string typeName = Marshal.PtrToStringUTF8(typeNamePtr)!;
                string instanceKey = Marshal.PtrToStringUTF8(instanceGuidPtr)!;

                bool created = GetContextOrThrow().CreateInstance(instanceKey, typeName);
                if (created)
                {
                    _hostHook?.Log($"Created instance {instanceKey}: {typeName}");
                }
                else
                {
                    _hostHook?.Log($"Reusing existing instance {instanceKey}: {typeName}");
                }
                return 1;
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"CreateInstanceGuid failed: {ex}");
                return 0;
            }
        }

        [UnmanagedCallersOnly]
        public static void DestroyInstance(int instanceId)
        {
            try
            {
                GetContextOrThrow().DestroyInstance(instanceId);
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"DestroyInstance failed: {ex}");
            }
        }

        [UnmanagedCallersOnly]
        public static void DestroyInstanceGuid(IntPtr instanceGuidPtr)
        {
            try
            {
                string instanceKey = Marshal.PtrToStringUTF8(instanceGuidPtr)!;
                GetContextOrThrow().DestroyInstance(instanceKey);
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"DestroyInstanceGuid failed: {ex}");
            }
        }

        // Bind an instance method and return a method handle.
        [UnmanagedCallersOnly]
        public static int BindInstanceMethod(int instanceId, IntPtr methodNamePtr, int signature)
        {
            try
            {
                string methodName = Marshal.PtrToStringUTF8(methodNamePtr)!;
                int id = GetContextOrThrow().BindInstanceMethod(instanceId, methodName, signature);
                _hostHook?.Log($"Bound instance method {id}: instance {instanceId}.{methodName} (sig={signature})");
                return id;
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"BindInstanceMethod failed: {ex}");
                return 0;
            }
        }

        // Bind an instance method using a caller-supplied instance key.
        [UnmanagedCallersOnly]
        public static int BindInstanceMethodGuid(IntPtr instanceGuidPtr, IntPtr methodNamePtr, int signature)
        {
            try
            {
                string instanceKey = Marshal.PtrToStringUTF8(instanceGuidPtr)!;
                string methodName = Marshal.PtrToStringUTF8(methodNamePtr)!;

                int id = GetContextOrThrow().BindInstanceMethod(instanceKey, methodName, signature);
                _hostHook?.Log($"Bound instance method {id}: instance {instanceKey}.{methodName} (sig={signature})");
                return id;
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"BindInstanceMethodGuid failed: {ex}");
                return 0;
            }
        }

        // Bind a static method and return a method handle.
        [UnmanagedCallersOnly]
        public static int BindStaticMethod(IntPtr typeNamePtr, IntPtr methodNamePtr, int signature)
        {
            try
            {
                string typeName = Marshal.PtrToStringUTF8(typeNamePtr)!;
                string methodName = Marshal.PtrToStringUTF8(methodNamePtr)!;
                int id = GetContextOrThrow().BindStaticMethod(typeName, methodName, signature);
                _hostHook?.Log($"Bound static method {id}: {typeName}.{methodName} (sig={signature})");
                return id;
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"BindStaticMethod failed: {ex}");
                return 0;
            }
        }

        [UnmanagedCallersOnly]
        public static int RegisterSignature(int signatureId, IntPtr returnTypeNamePtr, IntPtr parameterTypeNamePtrs, int parameterCount)
        {
            try
            {
                string returnTypeName = Marshal.PtrToStringUTF8(returnTypeNamePtr)!;
                var paramNames = parameterCount == 0 ? Array.Empty<string>() : new string[parameterCount];
                for (int i = 0; i < paramNames.Length; i++)
                {
                    IntPtr p = Marshal.ReadIntPtr(parameterTypeNamePtrs, i * IntPtr.Size);
                    paramNames[i] = Marshal.PtrToStringUTF8(p)!;
                }

                GetContextOrThrow().RegisterSignature(signatureId, returnTypeName, paramNames);
                _hostHook?.Log($"Registered signature {signatureId}: {returnTypeName}({string.Join(",", paramNames)})");
                return 1;
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"RegisterSignature failed: {ex}");
                return 0;
            }
        }

        // Generic invoke.
        // argsPtr points to an array of IntPtr, each element points to the value for that argument.
        // - int: pointer to int32
        // - float: pointer to float32
        // - bool: pointer to int32 (0/1)
        // - struct: pointer to struct bytes (LayoutKind.Sequential)
        // returnPtr:
        // - void: can be null
        // - int/bool: pointer to int32
        // - float: pointer to float32
        // - struct: pointer to struct bytes
        [UnmanagedCallersOnly]
        public static int Invoke(int methodId, IntPtr argsPtr, int argCount, IntPtr returnPtr)
        {
            try
            {
                GetContextOrThrow().Invoke(methodId, argsPtr, argCount, returnPtr);
                return 1;
            }
            catch (Exception ex)
            {
                _hostHook?.Log($"Invoke failed: {ex}");
                return 0;
            }
        }


        // Back-compat: previous API used by older native hosts.
        [UnmanagedCallersOnly]
        public static int LoadGameAssembly(IntPtr assemblyPathPtr)
        {
            string path = Marshal.PtrToStringUTF8(assemblyPathPtr)!;
            return LoadAssemblyCore(path);
        }

        // Back-compat: previously loaded and bound one method in managed.
        [UnmanagedCallersOnly]
        public static int LoadGameAssemblyEx(IntPtr assemblyPathPtr, IntPtr entryTypeNamePtr, IntPtr updateMethodNamePtr)
        {
            string path = Marshal.PtrToStringUTF8(assemblyPathPtr)!;
            _ = Marshal.PtrToStringUTF8(entryTypeNamePtr)!;
            _ = Marshal.PtrToStringUTF8(updateMethodNamePtr)!;
            // New flow: LoadAssembly + CreateInstance + Bind* + Invoke*.
            return LoadAssemblyCore(path);
        }

        [UnmanagedCallersOnly]
        public static void Update()
        {
            // Intentionally empty: the engine should invoke whatever methods it wants
            // using Invoke* APIs. Kept for older hosts that call Update().
        }
    }
}
