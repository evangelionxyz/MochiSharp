using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace MochiSharp.Managed.Core
{
	public sealed class ScriptContext
	{
        private string _serializeFieldAttributeTypeName = string.Empty;
		private string _entityTypeName = string.Empty;

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

				string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName)!;
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

		private class FieldAccessor
		{
			public required FieldInfo Field;
			public required Func<object, object?> Getter;
			public required Action<object, object?> Setter;
			public required bool IsPublic;
			public required bool HasSerializeFieldAttribute;
		}

		private readonly string _pluginPath;
		private readonly string _shadowDirectory;
		private readonly string _shadowAssemblyPath;
		private readonly PluginLoadContext _loadContext;
		private readonly Assembly _pluginAssembly;

		public string PluginPath => _pluginPath;

		private readonly Dictionary<ulong, object> _instances = new();

		private int _nextMethodId = 1;
		private readonly Dictionary<int, MethodBinding> _methods = new();
		private readonly Dictionary<Type, Dictionary<string, FieldAccessor>> _typeFieldAccessorCache = new();

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
			public readonly object Target;
			public readonly MethodInfo Method;
			public readonly Signature Signature;

			public MethodBinding(object target, MethodInfo method, Signature signature)
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

			_shadowDirectory = Path.Combine(Path.GetTempPath(), "MochiSharp", "shadow", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_shadowDirectory);
			_shadowAssemblyPath = Path.Combine(_shadowDirectory, Path.GetFileName(_pluginPath));
			File.Copy(_pluginPath, _shadowAssemblyPath, overwrite: true);

			_loadContext = new PluginLoadContext(_pluginPath, typeof(Bootstrap).Assembly);
			_pluginAssembly = _loadContext.LoadFromAssemblyPath(_shadowAssemblyPath);
		}

		public void ConfigureSerializationTypeNames(string serializeFieldAttributeTypeName, string entityTypeName)
		{
			_serializeFieldAttributeTypeName = serializeFieldAttributeTypeName?.Trim() ?? string.Empty;
			_entityTypeName = entityTypeName?.Trim() ?? string.Empty;
		}

		public void Unload()
		{
			_instances.Clear();
			_methods.Clear();
			_signatures.Clear();
			_loadContext.Unload();

			try
			{
				if (Directory.Exists(_shadowDirectory))
				{
					Directory.Delete(_shadowDirectory, recursive: true);
				}
			}
			catch
			{
			}
		}

		public string GetDerivedTypes(string baseTypeFullName)
		{
			if (string.IsNullOrWhiteSpace(baseTypeFullName))
			{
				return string.Empty;
			}

			var baseType = Type.GetType(baseTypeFullName, throwOnError: false)
				?? AppDomain.CurrentDomain.GetAssemblies()
					.Select(a => a.GetType(baseTypeFullName, throwOnError: false))
					.FirstOrDefault(t => t != null)
				?? throw new TypeLoadException($"Base type not found: {baseTypeFullName}");

			var derived = _pluginAssembly.GetTypes()
				.Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
				.Select(t => t.FullName)
				.Where(n => !string.IsNullOrEmpty(n))
				.ToArray();

			return string.Join("|", derived);
		}

		public string GetTypeFields(string typeName)
		{
			if (string.IsNullOrWhiteSpace(typeName))
			{
				return string.Empty;
			}

			Type type = ResolvePluginType(typeName);
			return BuildFieldMetadataPayload(type);
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

		public bool CreateInstance(ulong instanceId, string typeName)
		{
			if (instanceId == 0)
			{
				throw new ArgumentException("Instance id is required", nameof(instanceId));
			}

			if (_instances.TryGetValue(instanceId, out var existingInstance))
			{
				if (!string.Equals(existingInstance.GetType().FullName, typeName, StringComparison.Ordinal))
				{
					throw new InvalidOperationException($"Instance id {instanceId} already exists with type {existingInstance.GetType().FullName}, requested {typeName}");
				}

				return false;
			}

			Type type = ResolvePluginType(typeName);
			object instance = Activator.CreateInstance(type)
				?? throw new InvalidOperationException($"Failed to create instance of {type.FullName}");

			_instances.Add(instanceId, instance);
			return true;
		}

		public void DestroyInstance(ulong instanceId)
		{
			if (_instances.Remove(instanceId, out var obj))
			{
				if (obj is IDisposable d)
				{
					d.Dispose();
				}
			}
		}

		public string GetInstanceFields(ulong instanceId)
		{
			if (instanceId == 0)
			{
				throw new ArgumentException("Instance id is required", nameof(instanceId));
			}

			if (!_instances.TryGetValue(instanceId, out var existingInstance) || existingInstance == null)
			{
				throw new KeyNotFoundException($"Instance id not found: {instanceId}");
			}

			var type = existingInstance.GetType();
			return BuildFieldMetadataPayload(type);
		}

		public bool GetInstanceFieldValue(ulong instanceId, string fieldName, IntPtr buffer, int bufferSize)
		{
			if (!_instances.TryGetValue(instanceId, out var instance))
			{
				return false;
			}

			if (string.IsNullOrWhiteSpace(fieldName) || buffer == IntPtr.Zero || bufferSize <= 0)
			{
				return false;
			}

			if (!TryGetFieldAccessor(instance.GetType(), fieldName, out var accessor))
			{
				return false;
			}

			object? value = accessor.Getter(instance);
			return TryWriteFieldValueToBuffer(accessor.Field.FieldType, value, buffer, bufferSize);
		}

		public bool SetInstanceFieldValue(ulong instanceId, string fieldName, IntPtr buffer, int bufferSize)
		{
			if (!_instances.TryGetValue(instanceId, out var instance))
			{
				return false;
			}

			if (string.IsNullOrWhiteSpace(fieldName) || buffer == IntPtr.Zero || bufferSize <= 0)
			{
				return false;
			}

			if (!TryGetFieldAccessor(instance.GetType(), fieldName, out var accessor))
			{
				return false;
			}

			if (!TryReadFieldValueFromBuffer(accessor.Field.FieldType, buffer, bufferSize, out object? value))
			{
				return false;
			}

			accessor.Setter(instance, value);
			return true;
		}

		public int BindInstanceMethod(ulong instanceId, string methodName, int signatureId)
		{
			if (!_instances.TryGetValue(instanceId, out var instance))
			{
				throw new KeyNotFoundException($"Instance id not found: {instanceId}");
			}

			var type = instance.GetType();

			MethodInfo method;
			Signature sig;
			if (_signatures.TryGetValue(signatureId, out var registeredSig))
			{
				sig = registeredSig;
				method = FindMethod(type, methodName, sig.ParameterTypes, isStatic: false);
				EnsureReturnType(method, sig.ReturnType);
			}
			else
			{
				method = FindMethodByName(type, methodName, isStatic: false);
				sig = new Signature(method.ReturnType, method.GetParameters().Select(p => p.ParameterType).ToArray());
			}

			int id = _nextMethodId++;
			_methods.Add(id, new MethodBinding(instance, method, sig));
			return id;
		}

		public int BindStaticMethod(string typeName, string methodName, int signatureId)
		{
			Type type = ResolvePluginType(typeName);

			MethodInfo method;
			Signature sig;
			if (_signatures.TryGetValue(signatureId, out var registeredSig))
			{
				sig = registeredSig;
				method = FindMethod(type, methodName, sig.ParameterTypes, isStatic: true);
				EnsureReturnType(method, sig.ReturnType);
			}
			else
			{
				method = FindMethodByName(type, methodName, isStatic: true);
				sig = new Signature(method.ReturnType, method.GetParameters().Select(p => p.ParameterType).ToArray());
			}

			int id = _nextMethodId++;
			_methods.Add(id, new MethodBinding(null!, method, sig));
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

			object[] args = argCount == 0 ? Array.Empty<object>() : new object[argCount];
			for (int i = 0; i < argCount; i++)
			{
				IntPtr argValuePtr = Marshal.ReadIntPtr(argsPtr, i * IntPtr.Size);
				args[i] = ReadValueFromPointer(sig.ParameterTypes[i], argValuePtr);
			}

			object? result = binding.Method.Invoke(binding.Target, args);
			WriteReturnValueToPointer(sig.ReturnType, result!, returnPtr);
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

		private static MethodInfo FindMethodByName(Type type, string methodName, bool isStatic)
		{
			BindingFlags flags = (isStatic ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.NonPublic;
			var matches = type.GetMethods(flags)
				.Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
				.ToArray();

			if (matches.Length == 0)
			{
				throw new MissingMethodException($"Method not found: {type.FullName}.{methodName}");
			}

			if (matches.Length > 1)
			{
				throw new InvalidOperationException($"Multiple overloads found for {type.FullName}.{methodName}. Register and use explicit signature id.");
			}

			return matches[0];
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

			// Try referenced assemblies (e.g., IgniteScriptEngine from app assembly references).
			string pluginDir = Path.GetDirectoryName(_pluginPath) ?? string.Empty;
			foreach (var reference in _pluginAssembly.GetReferencedAssemblies())
			{
				Assembly? referencedAssembly = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => string.Equals(a.GetName().Name, reference.Name, StringComparison.OrdinalIgnoreCase));

				if (referencedAssembly == null)
				{
					try
					{
						referencedAssembly = Assembly.Load(reference);
					}
					catch
					{
						string candidate = Path.Combine(pluginDir, $"{reference.Name}.dll");
						if (File.Exists(candidate))
						{
							try
							{
								referencedAssembly = Assembly.LoadFrom(candidate);
							}
							catch
							{
							}
						}
					}
				}

				if (referencedAssembly == null)
				{
					continue;
				}

				t = referencedAssembly.GetType(typeName, throwOnError: false, ignoreCase: false);
				if (t != null)
				{
					return t;
				}
			}

			// Try all currently loaded assemblies.
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
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

		private string BuildFieldMetadataPayload(Type type)
		{
			if (!_typeFieldAccessorCache.TryGetValue(type, out var accessorsByName))
			{
				accessorsByName = BuildFieldAccessors(type);
				_typeFieldAccessorCache[type] = accessorsByName;
			}

			if (accessorsByName.Count == 0)
			{
				return string.Empty;
			}

			var builder = new StringBuilder();
			foreach (var accessor in accessorsByName.Values)
			{
				if (builder.Length > 0)
				{
					builder.Append('|');
				}

				builder.Append(accessor.Field.Name);
				builder.Append('~');
				builder.Append(accessor.Field.FieldType.FullName ?? accessor.Field.FieldType.Name);
				builder.Append('~');
				builder.Append(accessor.IsPublic ? '1' : '0');
				builder.Append('~');
				builder.Append(accessor.HasSerializeFieldAttribute ? '1' : '0');
			}

			return builder.ToString();
		}

		private bool TryGetFieldAccessor(Type type, string fieldName, out FieldAccessor accessor)
		{
			if (!_typeFieldAccessorCache.TryGetValue(type, out var accessorsByName))
			{
				accessorsByName = BuildFieldAccessors(type);
				_typeFieldAccessorCache[type] = accessorsByName;
			}

			return accessorsByName.TryGetValue(fieldName, out accessor!);
		}

		private Dictionary<string, FieldAccessor> BuildFieldAccessors(Type type)
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			var fields = type.GetFields(flags)
				.Where(f => IsSerializableField(f))
				.ToArray();

			var result = new Dictionary<string, FieldAccessor>(StringComparer.Ordinal);
			foreach (var field in fields)
			{
				bool hasSerializeField = HasSerializeFieldAttribute(field);
				result[field.Name] = new FieldAccessor
				{
					Field = field,
					Getter = CreateFieldGetter(field),
					Setter = CreateFieldSetter(field),
					IsPublic = field.IsPublic,
					HasSerializeFieldAttribute = hasSerializeField
				};
			}

			return result;
		}

        private bool IsSerializableField(FieldInfo field)
		{
			if (field.IsStatic || field.IsLiteral || field.IsInitOnly || field.IsSpecialName)
			{
				return false;
			}

			if (field.Name.StartsWith("<", StringComparison.Ordinal))
			{
				return false;
			}

			bool hasSerializeField = HasSerializeFieldAttribute(field);
			return field.IsPublic || hasSerializeField;
		}

		private bool HasSerializeFieldAttribute(MemberInfo member)
		{
			if (string.IsNullOrWhiteSpace(_serializeFieldAttributeTypeName))
			{
				return false;
			}

			return member.GetCustomAttributesData()
			  .Any(a => string.Equals(a.AttributeType.FullName, _serializeFieldAttributeTypeName, StringComparison.Ordinal));
		}

		private static Func<object, object?> CreateFieldGetter(FieldInfo field)
		{
			var dynamicMethod = new DynamicMethod(
				$"MochiSharp_Get_{field.DeclaringType?.FullName}_{field.Name}",
				typeof(object),
				new[] { typeof(object) },
				restrictedSkipVisibility: true);

			ILGenerator il = dynamicMethod.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Castclass, field.DeclaringType!);
			il.Emit(OpCodes.Ldfld, field);
			if (field.FieldType.IsValueType)
			{
				il.Emit(OpCodes.Box, field.FieldType);
			}
			il.Emit(OpCodes.Ret);

			return (Func<object, object?>)dynamicMethod.CreateDelegate(typeof(Func<object, object?>));
		}

		private static Action<object, object?> CreateFieldSetter(FieldInfo field)
		{
			var dynamicMethod = new DynamicMethod(
				$"MochiSharp_Set_{field.DeclaringType?.FullName}_{field.Name}",
				typeof(void),
				new[] { typeof(object), typeof(object) },
				restrictedSkipVisibility: true);

			ILGenerator il = dynamicMethod.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Castclass, field.DeclaringType!);
			il.Emit(OpCodes.Ldarg_1);
			if (field.FieldType.IsValueType)
			{
				il.Emit(OpCodes.Unbox_Any, field.FieldType);
			}
			else
			{
				il.Emit(OpCodes.Castclass, field.FieldType);
			}
			il.Emit(OpCodes.Stfld, field);
			il.Emit(OpCodes.Ret);

			return (Action<object, object?>)dynamicMethod.CreateDelegate(typeof(Action<object, object?>));
		}

        private bool TryWriteFieldValueToBuffer(Type fieldType, object? value, IntPtr buffer, int bufferSize)
		{
			if (fieldType == typeof(string))
			{
				string str = value as string ?? string.Empty;
				byte[] bytes = Encoding.UTF8.GetBytes(str);
				int copyLen = Math.Min(bytes.Length, bufferSize - 1);
				if (copyLen > 0)
				{
					Marshal.Copy(bytes, 0, buffer, copyLen);
				}
				Marshal.WriteByte(buffer, copyLen, 0);
				return true;
			}

			if (fieldType == typeof(bool))
			{
				if (bufferSize < sizeof(byte)) return false;
				Marshal.WriteByte(buffer, (bool)(value ?? false) ? (byte)1 : (byte)0);
				return true;
			}

			if (fieldType == typeof(byte))
			{
				if (bufferSize < sizeof(byte)) return false;
				Marshal.WriteByte(buffer, value is byte b ? b : (byte)0);
				return true;
			}

			if (fieldType == typeof(sbyte))
			{
				if (bufferSize < sizeof(sbyte)) return false;
				Marshal.WriteByte(buffer, unchecked((byte)(value is sbyte sb ? sb : (sbyte)0)));
				return true;
			}

			if (fieldType == typeof(short))
			{
				if (bufferSize < sizeof(short)) return false;
				Marshal.WriteInt16(buffer, value is short s ? s : (short)0);
				return true;
			}

			if (fieldType == typeof(ushort))
			{
				if (bufferSize < sizeof(ushort)) return false;
				Marshal.WriteInt16(buffer, unchecked((short)(value is ushort us ? us : (ushort)0)));
				return true;
			}

			if (fieldType == typeof(char))
			{
				if (bufferSize < sizeof(char)) return false;
				Marshal.WriteInt16(buffer, value is char c ? c : '\0');
				return true;
			}

			if (fieldType == typeof(int))
			{
				if (bufferSize < sizeof(int)) return false;
				Marshal.WriteInt32(buffer, value is int i ? i : 0);
				return true;
			}

			if (fieldType == typeof(uint))
			{
				if (bufferSize < sizeof(uint)) return false;
				Marshal.WriteInt32(buffer, unchecked((int)(value is uint ui ? ui : 0u)));
				return true;
			}

			if (fieldType == typeof(long))
			{
				if (bufferSize < sizeof(long)) return false;
				Marshal.WriteInt64(buffer, value is long l ? l : 0L);
				return true;
			}

			if (fieldType == typeof(ulong))
			{
				if (bufferSize < sizeof(ulong)) return false;
				Marshal.WriteInt64(buffer, unchecked((long)(value is ulong ul ? ul : 0UL)));
				return true;
			}

			if (fieldType == typeof(float))
			{
				if (bufferSize < sizeof(float)) return false;
				Marshal.StructureToPtr(value is float f ? f : 0.0f, buffer, fDeleteOld: false);
				return true;
			}

			if (fieldType == typeof(double))
			{
				if (bufferSize < sizeof(double)) return false;
				Marshal.StructureToPtr(value is double d ? d : 0.0, buffer, fDeleteOld: false);
				return true;
			}

			if (IsEntityFieldType(fieldType))
			{
				if (bufferSize < sizeof(ulong)) return false;
				ulong id = 0;
				if (value != null)
				{
					var idProperty = value.GetType().GetProperty("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (idProperty?.PropertyType == typeof(ulong))
					{
						id = (ulong)(idProperty.GetValue(value) ?? 0UL);
					}
				}
				Marshal.WriteInt64(buffer, unchecked((long)id));
				return true;
			}

			if (fieldType.IsValueType)
			{
				int typeSize = Marshal.SizeOf(fieldType);
				if (bufferSize < typeSize)
				{
					return false;
				}

				object boxed = value ?? Activator.CreateInstance(fieldType)!;
				Marshal.StructureToPtr(boxed, buffer, fDeleteOld: false);
				return true;
			}

			return false;
		}

       private bool TryReadFieldValueFromBuffer(Type fieldType, IntPtr buffer, int bufferSize, out object? value)
		{
			value = null;

			if (fieldType == typeof(string))
			{
				int len = 0;
				while (len < bufferSize && Marshal.ReadByte(buffer, len) != 0)
				{
					len++;
				}
				
				if (len == 0)
				{
					value = string.Empty;
					return true;
				}
				
				byte[] bytes = new byte[len];
				Marshal.Copy(buffer, bytes, 0, len);
				value = Encoding.UTF8.GetString(bytes);
				return true;
			}

			if (fieldType == typeof(bool))
			{
				if (bufferSize < sizeof(byte)) return false;
				value = Marshal.ReadByte(buffer) != 0;
				return true;
			}

			if (fieldType == typeof(byte))
			{
				if (bufferSize < sizeof(byte)) return false;
				value = Marshal.ReadByte(buffer);
				return true;
			}

			if (fieldType == typeof(sbyte))
			{
				if (bufferSize < sizeof(sbyte)) return false;
				value = unchecked((sbyte)Marshal.ReadByte(buffer));
				return true;
			}

			if (fieldType == typeof(short))
			{
				if (bufferSize < sizeof(short)) return false;
				value = Marshal.ReadInt16(buffer);
				return true;
			}

			if (fieldType == typeof(ushort))
			{
				if (bufferSize < sizeof(ushort)) return false;
				value = unchecked((ushort)Marshal.ReadInt16(buffer));
				return true;
			}

			if (fieldType == typeof(char))
			{
				if (bufferSize < sizeof(char)) return false;
				value = unchecked((char)Marshal.ReadInt16(buffer));
				return true;
			}

			if (fieldType == typeof(int))
			{
				if (bufferSize < sizeof(int)) return false;
				value = Marshal.ReadInt32(buffer);
				return true;
			}

			if (fieldType == typeof(uint))
			{
				if (bufferSize < sizeof(uint)) return false;
				value = unchecked((uint)Marshal.ReadInt32(buffer));
				return true;
			}

			if (fieldType == typeof(long))
			{
				if (bufferSize < sizeof(long)) return false;
				value = Marshal.ReadInt64(buffer);
				return true;
			}

			if (fieldType == typeof(ulong))
			{
				if (bufferSize < sizeof(ulong)) return false;
				value = unchecked((ulong)Marshal.ReadInt64(buffer));
				return true;
			}

			if (fieldType == typeof(float))
			{
				if (bufferSize < sizeof(float)) return false;
				value = Marshal.PtrToStructure<float>(buffer);
				return true;
			}

			if (fieldType == typeof(double))
			{
				if (bufferSize < sizeof(double)) return false;
				value = Marshal.PtrToStructure<double>(buffer);
				return true;
			}

			if (IsEntityFieldType(fieldType))
			{
				if (bufferSize < sizeof(ulong)) return false;
				ulong id = unchecked((ulong)Marshal.ReadInt64(buffer));
				if (id == 0)
				{
					value = null;
					return true;
				}

				var ctor = fieldType.GetConstructor(
					BindingFlags.Instance | BindingFlags.NonPublic,
					binder: null,
					types: new[] { typeof(ulong) },
					modifiers: null);

				if (ctor == null)
				{
					return false;
				}

				value = ctor.Invoke(new object[] { id });
				return true;
			}

			if (fieldType.IsValueType)
			{
				int typeSize = Marshal.SizeOf(fieldType);
				if (bufferSize < typeSize)
				{
					return false;
				}

				value = Marshal.PtrToStructure(buffer, fieldType);
				return value != null;
			}

			return false;
		}

        private bool IsEntityFieldType(Type type)
		{
          return !string.IsNullOrWhiteSpace(_entityTypeName) && string.Equals(type.FullName, _entityTypeName, StringComparison.Ordinal);
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

		private static void WriteReturnValueToPointer(Type returnType, object result, IntPtr returnPtr)
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

