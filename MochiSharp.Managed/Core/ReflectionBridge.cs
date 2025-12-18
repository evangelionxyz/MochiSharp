using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MochiSharp.Managed.Core;

/// <summary>
/// Reflection utilities that can be called from native code via delegates.
/// Provides lightweight type metadata for tooling and scripting diagnostics.
/// </summary>
public static class ReflectionBridge
{
    private const BindingFlags Binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private sealed record ParameterMetadata(string Name, string Type);
    private sealed record MethodMetadata(string Name, string ReturnType, ParameterMetadata[] Parameters);
    private sealed record FieldMetadata(string Name, string Type, bool IsPublic, bool IsStatic);
    private sealed record TypeMetadata(string Name, string FullName, string? BaseType, FieldMetadata[] Fields, MethodMetadata[] Methods);

    /// <summary>
    /// Returns UTF-8 JSON describing the requested managed type. If <paramref name="buffer"/> is null
    /// or <paramref name="bufferSize"/> is zero, the required byte length (including null terminator) is returned.
    /// </summary>
    /// <param name="typeName">Full type name (namespace + type)</param>
    /// <param name="buffer">Destination buffer for UTF-8 JSON (may be IntPtr.Zero to query size)</param>
    /// <param name="bufferSize">Size in bytes of <paramref name="buffer"/></param>
    /// <returns>Total required size in bytes including null terminator.</returns>
    public static int DescribeType(string typeName, IntPtr buffer, int bufferSize)
    {
        var metadata = BuildTypeMetadata(typeName);
        var json = JsonSerializer.Serialize(metadata);
        var bytes = Encoding.UTF8.GetBytes(json);

        // Required size includes null terminator
        var requiredSize = bytes.Length + 1;
        if (buffer == IntPtr.Zero || bufferSize <= 0)
        {
            return requiredSize;
        }

        var bytesToCopy = Math.Min(bytes.Length, Math.Max(0, bufferSize - 1));
        Marshal.Copy(bytes, 0, buffer, bytesToCopy);
        Marshal.WriteByte(buffer, bytesToCopy, 0); // null terminator
        return requiredSize;
    }

    private static TypeMetadata BuildTypeMetadata(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type == null)
        {
            return new TypeMetadata(typeName, typeName, null, Array.Empty<FieldMetadata>(), Array.Empty<MethodMetadata>());
        }

        var fields = type
            .GetFields(Binding)
            .Select(f => new FieldMetadata(f.Name, f.FieldType.Name, f.IsPublic, f.IsStatic))
            .ToArray();

        var methods = type
            .GetMethods(Binding)
            .Where(m => !m.IsSpecialName)
            .Select(m => new MethodMetadata(
                m.Name,
                m.ReturnType.Name,
                m.GetParameters()
                 .Select(p => new ParameterMetadata(p.Name ?? string.Empty, p.ParameterType.Name))
                 .ToArray()))
            .ToArray();

        return new TypeMetadata(
            type.Name,
            type.FullName ?? type.Name,
            type.BaseType?.FullName,
            fields,
            methods);
    }
}
