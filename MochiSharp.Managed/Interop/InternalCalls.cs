using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MochiSharp.Managed.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct InternalCall
{
    private readonly IntPtr m_NamePtr;
    public readonly IntPtr NativeFunctionPtr;

    public string? Name => Marshal.PtrToStringAuto(m_NamePtr);
}

internal static class InternalCallsManager
{
    [UnmanagedCallersOnly]
    internal static void SetInternalCalls(int InAssemblyLoadContextId, IntPtr InInternalCalls, int InLength)
    {
        var internalCalls = new NativeArray<InternalCall>(InInternalCalls, IntPtr.Zero, InLength);

        try
        {
            for (int i = 0; i < internalCalls.Length; i++)
            {
                var internalCall = internalCalls[i];
                var name = internalCall.Name;

                if (name == null)
                {
                    ManagedHost.LogMessage($"Cannot register internal at index '{i}' call with null name!", MessageLevel.Error);
                    continue;
                }

                var fieldNameStart = name.IndexOf('+');
                var fieldNameEnd = name.IndexOf(",", fieldNameStart, StringComparison.CurrentCulture);
                var fieldName = name.Substring(fieldNameStart + 1, fieldNameEnd - fieldNameStart - 1);
                var containingTypeName = name.Remove(fieldNameStart, fieldNameEnd - fieldNameStart);

                var type = TypeInterface.FindType(InAssemblyLoadContextId, containingTypeName);

                if (type == null)
                {
                    ManagedHost.LogMessage($"Cannot register internal call '{name}', failed to type '{containingTypeName}'.", MessageLevel.Error);
                    continue;
                }

                var bindingFlags = BindingFlags.Static | BindingFlags.NonPublic;
                var field = type.GetFields(bindingFlags).FirstOrDefault(field => field.Name == fieldName);

                if (field == null)
                {
                    ManagedHost.LogMessage($"Cannot register internal '{name}', failed to find it in type '{containingTypeName}'", MessageLevel.Error);
                    continue;
                }

                if (!field.FieldType.IsFunctionPointer)
                {
                    ManagedHost.LogMessage($"Field '{name}' is not a function pointer type!", MessageLevel.Error);
                    continue;
                }

                field.SetValue(null, internalCall.NativeFunctionPtr);
            }
        }
        catch (Exception ex)
        {
            ManagedHost.HandleException(ex);
        }
    }
}
