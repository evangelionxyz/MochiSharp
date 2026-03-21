using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MochiSharp.Managed.Core
{
	public sealed class ScriptContext
	{
		private sealed class PluginLoadContext : AssemblyLoadContext
		{
			private readonly AssemblyDependencyResolver _resolver;
			private readonly Assembly _coreAssembly;

			public PluginLoadContext(string mainAssemblyPath, Assembly coreAssembly)
				: base($"MochiSharp.Plugin:{Path.GetFileNameWithoutExtension(mainAssemblyPath)}", isCollectible: true)
			{
				_resolver = new AssemblyDependencyResolver(mainAssemblyPath);
				_coreAssembly = coreAssembly;
			}

			protected override Assembly? Load(AssemblyName assemblyName)
			{
				// Ensure the core is always shared from the default context to avoid type identity issues.
				if (string.Equals(assemblyName.Name, _coreAssembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
				{
					return _coreAssembly;
				}

				string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
				if (assemblyPath == null)
				{
					return null;
				}

				// Prefer already-loaded assemblies from the default context.
				foreach (var asm in AssemblyLoadContext.Default.Assemblies)
				{
					var name = asm.GetName();
					if (string.Equals(name.Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
					{
						return asm;
					}
				}

				return LoadFromAssemblyPath(assemblyPath);
			}

			protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
			{
				string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
				if (libraryPath == null)
				{
					return IntPtr.Zero;
				}

				return LoadUnmanagedDllFromPath(libraryPath);
			}
		}

		private readonly string _pluginPath;
		private readonly PluginLoadContext _loadContext;
		private readonly Assembly _pluginAssembly;

		private int _nextInstanceId = 1;
		private readonly Dictionary<int, object> _instances = new();
		private readonly Dictionary<string, object> _instancesByGuid = new(StringComparer.Ordinal);

		private int _nextMethodId = 1;
		private readonly Dictionary<int, MethodBinding> _methods = new();

		private readonly Dictionary<int, Signature> _signatures = new();

		private readonly struct Signature
		{
			public readonly Type ReturnType;
			public readonly Type[] ParameterTypes;

			public Signature(Type returnType, Type[] parameterTypes)
			{
				ReturnType = returnType;
				ParameterTypes = parameterTypes;
			}
		}

		private readonly struct MethodBinding
		{
			public readonly object? Target;
			public readonly MethodInfo Method;
			public readonly Signature Signature;

			public MethodBinding(object? target, MethodInfo method, Signature signature)
			{
				Target = target;
				Method = method;
				Signature = signature;
			}
		}

		public ScriptContext(string pluginAssemblyPath)
		{
			if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
			{
				throw new ArgumentException("Plugin assembly path is required", nameof(pluginAssemblyPath));
			}

			_pluginPath = Path.GetFullPath(pluginAssemblyPath);
			if (!File.Exists(_pluginPath))
			{
				throw new FileNotFoundException("Plugin assembly not found", _pluginPath);
			}

			_loadContext = new PluginLoadContext(_pluginPath, typeof(Bootstrap).Assembly);
			_pluginAssembly = _loadContext.LoadFromAssemblyPath(_pluginPath);
		}

		public void Unload()
		{
			_instances.Clear();
			_instancesByGuid.Clear();
			_methods.Clear();
			_signatures.Clear();
			_loadContext.Unload();
		}

		public void RegisterSignature(int signatureId, string returnTypeName, string[] parameterTypeNames)
		{
            ArgumentOutOfRangeException.ThrowIfNegative(signatureId);

            Type returnType = ResolveType(returnTypeName);
			var paramTypes = parameterTypeNames.Length == 0
				? Array.Empty<Type>()
				: parameterTypeNames.Select(ResolveType).ToArray();

			_signatures[signatureId] = new Signature(returnType, paramTypes);
		}

		public int CreateInstance(string typeName)
		{
			Type type = ResolvePluginType(typeName);
			object instance = Activator.CreateInstance(type)
				?? throw new InvalidOperationException($"Failed to create instance of {type.FullName}");

			int id = _nextInstanceId++;
			_instances.Add(id, instance);
			return id;
		}

        public void CreateInstance(string instanceId, string typeName)
		{
           if (string.IsNullOrWhiteSpace(instanceId))
			{
				throw new ArgumentException("Instance id is required", nameof(instanceId));
			}

			if (_instancesByGuid.ContainsKey(instanceId))
			{
             throw new InvalidOperationException($"Instance id already exists: {instanceId}");
			}

			Type type = ResolvePluginType(typeName);
			object instance = Activator.CreateInstance(type)
				?? throw new InvalidOperationException($"Failed to create instance of {type.FullName}");

			_instancesByGuid.Add(instanceId, instance);
		}

		public void DestroyInstance(int instanceId)
		{
			if (_instances.Remove(instanceId, out var obj))
			{
				if (obj is IDisposable d)
				{
					d.Dispose();
				}
			}
		}

        public void DestroyInstance(string instanceId)
		{
			if (_instancesByGuid.Remove(instanceId, out var obj))
			{
				if (obj is IDisposable d)
				{
					d.Dispose();
				}
			}
		}

		public int BindInstanceMethod(int instanceId, string methodName, int signatureId)
		{
			if (!_instances.TryGetValue(instanceId, out var instance))
			{
				throw new KeyNotFoundException($"Instance id not found: {instanceId}");
			}

			Signature sig = GetSignature(signatureId);
			var type = instance.GetType();
			var method = FindMethod(type, methodName, sig.ParameterTypes, isStatic: false);
			EnsureReturnType(method, sig.ReturnType);

			int id = _nextMethodId++;
			_methods.Add(id, new MethodBinding(instance, method, sig));
			return id;
		}

      public int BindInstanceMethod(string instanceId, string methodName, int signatureId)
		{
			if (!_instancesByGuid.TryGetValue(instanceId, out var instance))
			{
               throw new KeyNotFoundException($"Instance id not found: {instanceId}");
			}

			Signature sig = GetSignature(signatureId);
			var type = instance.GetType();
			var method = FindMethod(type, methodName, sig.ParameterTypes, isStatic: false);
			EnsureReturnType(method, sig.ReturnType);

			int id = _nextMethodId++;
			_methods.Add(id, new MethodBinding(instance, method, sig));
			return id;
		}

		public int BindStaticMethod(string typeName, string methodName, int signatureId)
		{
			Type type = ResolvePluginType(typeName);
			Signature sig = GetSignature(signatureId);
			var method = FindMethod(type, methodName, sig.ParameterTypes, isStatic: true);
			EnsureReturnType(method, sig.ReturnType);

			int id = _nextMethodId++;
			_methods.Add(id, new MethodBinding(null, method, sig));
			return id;
		}

		public void Invoke(int methodId, IntPtr argsPtr, int argCount, IntPtr returnPtr)
		{
			if (!_methods.TryGetValue(methodId, out var binding))
			{
				throw new KeyNotFoundException($"Method id not found: {methodId}");
			}

			var sig = binding.Signature;
			if (argCount != sig.ParameterTypes.Length)
			{
				throw new ArgumentException($"Argument count mismatch. Expected {sig.ParameterTypes.Length}, got {argCount}");
			}

			object?[] args = argCount == 0 ? Array.Empty<object?>() : new object?[argCount];
			for (int i = 0; i < argCount; i++)
			{
				IntPtr argValuePtr = Marshal.ReadIntPtr(argsPtr, i * IntPtr.Size);
				args[i] = ReadValueFromPointer(sig.ParameterTypes[i], argValuePtr);
			}

			object? result = binding.Method.Invoke(binding.Target, args);
			WriteReturnValueToPointer(sig.ReturnType, result, returnPtr);
		}

        private Signature GetSignature(int signatureId)
		{
			if (!_signatures.TryGetValue(signatureId, out var sig))
			{
				throw new InvalidOperationException($"Signature not registered: {signatureId}");
			}

			return sig;
		}

		private static void EnsureReturnType(MethodInfo method, Type expectedReturnType)
		{
			if (method.ReturnType != expectedReturnType)
			{
				throw new InvalidOperationException($"Return type mismatch for {method.DeclaringType?.FullName}.{method.Name}. Expected {expectedReturnType}, got {method.ReturnType}");
			}
		}

		private static MethodInfo FindMethod(Type type, string methodName, Type[] parameterTypes, bool isStatic)
		{
			BindingFlags flags = (isStatic ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.NonPublic;

			var method = type.GetMethod(methodName, flags, binder: null, types: parameterTypes, modifiers: null);
			if (method != null)
			{
				return method;
			}

			// Fallback: manually scan in case of binder quirks.
			foreach (var m in type.GetMethods(flags))
			{
				if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
				{
					continue;
				}

				var ps = m.GetParameters();
				if (ps.Length != parameterTypes.Length)
				{
					continue;
				}

				bool ok = true;
				for (int i = 0; i < ps.Length; i++)
				{
					if (ps[i].ParameterType != parameterTypes[i])
					{
						ok = false;
						break;
					}
				}

				if (ok)
				{
					return m;
				}
			}

			throw new MissingMethodException($"Method not found: {type.FullName}.{methodName}({string.Join(",", parameterTypes.Select(t => t.FullName))})");
		}

		private Type ResolvePluginType(string typeName)
		{
			// Prefer plugin assembly resolution so scripts stay in the collectible context.
			var t = _pluginAssembly.GetType(typeName, throwOnError: false, ignoreCase: false);
			if (t != null)
			{
				return t;
			}

			// Fallback: try loaded assemblies in the plugin ALC.
			foreach (var asm in _loadContext.Assemblies)
			{
				t = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
				if (t != null)
				{
					return t;
				}
			}

			// Last resort.
			t = Type.GetType(typeName, throwOnError: false);
			if (t != null)
			{
				return t;
			}

			throw new TypeLoadException($"Type not found: {typeName}");
		}

		private Type ResolveType(string typeName)
		{
			if (string.IsNullOrWhiteSpace(typeName))
			{
				throw new ArgumentException("Type name required", nameof(typeName));
			}

			string n = typeName.Trim();
          bool isByRef = false;
			if (n.EndsWith("&", StringComparison.Ordinal))
			{
				isByRef = true;
				n = n[..^1].Trim();
			}

			string fullName = n;
			string? assemblyPart = null;
			int commaIndex = n.IndexOf(',');
			if (commaIndex >= 0)
			{
				fullName = n[..commaIndex].Trim();
				assemblyPart = n[(commaIndex + 1)..].Trim();
				if (fullName.EndsWith("&", StringComparison.Ordinal))
				{
					isByRef = true;
					fullName = fullName[..^1].Trim();
				}
			}

			if (string.Equals(n, "void", StringComparison.OrdinalIgnoreCase) || string.Equals(n, typeof(void).FullName, StringComparison.Ordinal))
			{
				return typeof(void);
			}
			if (string.Equals(n, "int", StringComparison.OrdinalIgnoreCase) || string.Equals(n, typeof(int).FullName, StringComparison.Ordinal))
			{
				return typeof(int);
			}
			if (string.Equals(n, "float", StringComparison.OrdinalIgnoreCase) || string.Equals(n, typeof(float).FullName, StringComparison.Ordinal))
			{
				return typeof(float);
			}
			if (string.Equals(n, "bool", StringComparison.OrdinalIgnoreCase) || string.Equals(n, typeof(bool).FullName, StringComparison.Ordinal))
			{
				return typeof(bool);
			}

           Type? t = null;

			// Try standard resolution first.
         t = Type.GetType(typeName.Trim(), throwOnError: false);
			if (t != null)
			{
               return isByRef && !t.IsByRef ? t.MakeByRefType() : t;
			}

			if (!string.IsNullOrWhiteSpace(assemblyPart))
			{
				var loadedAsm = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyPart, StringComparison.OrdinalIgnoreCase));

				if (loadedAsm != null)
				{
					t = loadedAsm.GetType(fullName, throwOnError: false, ignoreCase: false);
					if (t != null)
					{
						return isByRef ? t.MakeByRefType() : t;
					}
				}

				try
				{
					var asmName = new AssemblyName(assemblyPart);
					loadedAsm = Assembly.Load(asmName);
					t = loadedAsm?.GetType(fullName, throwOnError: false, ignoreCase: false);
					if (t != null)
					{
						return isByRef ? t.MakeByRefType() : t;
					}
				}
				catch
				{
				}

				try
				{
					string candidate = Path.Combine(Path.GetDirectoryName(_pluginPath) ?? string.Empty, $"{assemblyPart}.dll");
					if (File.Exists(candidate))
					{
						loadedAsm = Assembly.LoadFrom(candidate);
						t = loadedAsm.GetType(fullName, throwOnError: false, ignoreCase: false);
						if (t != null)
						{
							return isByRef ? t.MakeByRefType() : t;
						}
					}
				}
				catch
				{
				}
			}

            // Try in plugin ALC assemblies.
			foreach (var asm in _loadContext.Assemblies)
			{
				t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
				if (t != null)
				{
                   return isByRef ? t.MakeByRefType() : t;
				}
			}

			// Try AppDomain assemblies too (covers BCL and already loaded deps).
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
				if (t != null)
				{
                   return isByRef ? t.MakeByRefType() : t;
				}
			}

			throw new TypeLoadException($"Unable to resolve type: {typeName}");
		}

		private static object ReadValueFromPointer(Type type, IntPtr ptr)
		{
			if (type == typeof(int))
			{
				return Marshal.ReadInt32(ptr);
			}

			if (type == typeof(float))
			{
				return Marshal.PtrToStructure<float>(ptr);
			}

			if (type == typeof(bool))
			{
				return Marshal.ReadInt32(ptr) != 0;
			}

			if (type.IsValueType)
			{
				return Marshal.PtrToStructure(ptr, type) ?? throw new InvalidOperationException($"Failed to marshal {type}");
			}

			throw new NotSupportedException($"Unsupported parameter type: {type}");
		}

		private static void WriteReturnValueToPointer(Type returnType, object? result, IntPtr returnPtr)
		{
			if (returnType == typeof(void))
			{
				return;
			}

			if (returnPtr == IntPtr.Zero)
			{
				throw new ArgumentException("Return pointer must be non-null for non-void return");
			}

			if (returnType == typeof(int))
			{
				Marshal.WriteInt32(returnPtr, result is int i ? i : 0);
				return;
			}

			if (returnType == typeof(float))
			{
				float f = result is float ff ? ff : 0.0f;
				Marshal.StructureToPtr(f, returnPtr, fDeleteOld: false);
				return;
			}

			if (returnType == typeof(bool))
			{
				bool b = result is bool bb && bb;
				Marshal.WriteInt32(returnPtr, b ? 1 : 0);
				return;
			}

			if (returnType.IsValueType)
			{
				if (result == null)
				{
					throw new InvalidOperationException($"Method returned null for value type {returnType}");
				}

				Marshal.StructureToPtr(result, returnPtr, fDeleteOld: false);
				return;
			}

			throw new NotSupportedException($"Unsupported return type: {returnType}");
		}
	}
}

