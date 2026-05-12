using MochiSharp.Managed.Interop;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MochiSharp.Managed;

public static class Marshalling
{
#pragma warning disable 0649
    // This needs to map to MochiSharp::Array
    struct ValueArrayContainer
    {
        public IntPtr Data;
        public IntPtr ArrayHandle;
        public int Length;
    }

    // This needs to map to MochiSharp::Array
    struct ObjectArrayContainer
    {
        public IntPtr Data;
        public IntPtr ArrayHandle;
        public int Length;
    }

    private struct ArrayObject
    {
        public IntPtr Handle;
        public IntPtr Padding;
    }
#pragma warning restore 0649

    public static void MarshalReturnValue(object? target, object? value, MemberInfo? memberInfo, IntPtr outValue)
    {
        if (memberInfo == null)
            return;

        Type? type = null;

        if (memberInfo is FieldInfo fieldInfo)
        {
            type = fieldInfo.FieldType;
        }
        else if (memberInfo is PropertyInfo propertyInfo)
        {
            type = propertyInfo.PropertyType;
        }
        else if (memberInfo is MethodInfo methodInfo)
        {
            type = methodInfo.ReturnType;
        }

        if (type != null && type.IsSZArray)
        {
            var fieldArray = ArrayStorage.GetFieldArray(target, value, memberInfo);
            if (fieldArray != null)
            {
                Marshal.WriteIntPtr(outValue, fieldArray.Value.AddrOfPinnedObject());
            }
            else
            {
                Marshal.WriteIntPtr(outValue, IntPtr.Zero);
            }
        }
        else if (type == typeof(string) && value != null)
        {
            NativeString nativeString = (NativeString)(string)value;
            Marshal.StructureToPtr(nativeString, outValue, false);
        }
        else if (type == typeof(bool) && value != null)
        {
            Bool32 bValue = (Bool32)(bool)value;
            Marshal.StructureToPtr(bValue, outValue, false);
        }
        else if (type == typeof(NativeString) && value != null)
        {
            NativeString nativeString = (NativeString)value;
            Marshal.StructureToPtr(nativeString, outValue, false);
        }
        else if (type != null && type.IsPointer)
        {
            unsafe
            {
                if (value == null)
                {
                    Marshal.WriteIntPtr(outValue, IntPtr.Zero);
                }
                else
                {
                    void* valuePointer = Pointer.Unbox(value);
                    Buffer.MemoryCopy(&valuePointer, outValue.ToPointer(), IntPtr.Size, IntPtr.Size);
                }
            }
        }
        else if (type != null)
        {
            int valueSize = type.IsEnum ? Marshal.SizeOf(Enum.GetUnderlyingType(type)) : Marshal.SizeOf(type);
            GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);

            unsafe
            {
                Buffer.MemoryCopy(handle.AddrOfPinnedObject().ToPointer(), outValue.ToPointer(), valueSize, valueSize);
            }

            handle.Free();
        }
        else throw new ArgumentNullException("memberInfo:Type");
    }

    public static object? MarshalArray(IntPtr array, Type? elemType)
    {
        if (elemType == null)
            return null;

        Array? result;

        if (elemType.IsValueType)
        {
            var arrayContainer = MarshalPointer<ValueArrayContainer>(array);

            if (ArrayStorage.HasFieldArray(null, null))
            {
                var fieldArray = ArrayStorage.GetFieldArray(null, null, null);

                if (arrayContainer.Data == fieldArray!.Value.AddrOfPinnedObject())
                {
                    return fieldArray.Value.Target;
                }
            }

            result = Array.CreateInstance(elemType, arrayContainer.Length);

            int elementSize = Marshal.SizeOf(elemType);

            unsafe
            {
                for (int i = 0; i < arrayContainer.Length; i++)
                {
                    IntPtr source = (IntPtr)(((byte*)arrayContainer.Data.ToPointer()) + (i * elementSize));
                    result.SetValue(Marshal.PtrToStructure(source, elemType), i);
                }
            }
        }
        else
        {
            var arrayContainer = MarshalPointer<ObjectArrayContainer>(array);

            if (ArrayStorage.HasFieldArray(null, null))
            {
                var fieldArray = ArrayStorage.GetFieldArray(null, null, null);

                if (arrayContainer.Data == fieldArray!.Value.AddrOfPinnedObject())
                {
                    return fieldArray.Value.Target;
                }
            }

            result = Array.CreateInstance(elemType, arrayContainer.Length);

            unsafe
            {
                for (int i = 0; i < arrayContainer.Length; i++)
                {
                    IntPtr source = (IntPtr)(((byte*)arrayContainer.Data.ToPointer()) + (i * Marshal.SizeOf<ArrayObject>()));
                    var managedObject = MarshalPointer<ArrayObject>(source);
                    var target = GCHandle.FromIntPtr(managedObject.Handle).Target;
                    result.SetValue(target, i);
                }
            }
        }

        return result;
    }

    public static object? MarshalPointer(IntPtr value, Type type)
    {
        if (type.IsPointer || type == typeof(IntPtr))
            return value;

        if (type == typeof(bool))
            return Marshal.PtrToStructure<byte>(value) > 0;

        if (type == typeof(string))
        {
            var nativeString = Marshal.PtrToStructure<NativeString>(value);
            return nativeString.ToString();
        }
        else if (type == typeof(NativeString))
        {
            return Marshal.PtrToStructure<NativeString>(value);
        }

        if (type.IsSZArray)
            return MarshalArray(value, type.GetElementType());

        if (type.IsGenericType)
        {
            if (type == typeof(NativeArray<>).MakeGenericType(type.GetGenericArguments().First()))
            {
                var elements = Marshal.ReadIntPtr(value, 0);
                var elementCount = Marshal.ReadInt32(value, Marshal.SizeOf<IntPtr>());
                var genericType = typeof(NativeArray<>).MakeGenericType(type.GetGenericArguments().First());
                return TypeInterface.CreateInstance(genericType, elements, elementCount);
            }
        }

        if (type.IsClass)
        {
            var handlePtr = Marshal.ReadIntPtr(value);
            var handle = GCHandle.FromIntPtr(handlePtr);
            return handle.Target;
        }

        return Marshal.PtrToStructure(value, type);
    }
    public static T? MarshalPointer<T>(IntPtr InValue) => Marshal.PtrToStructure<T>(InValue);

    public static IntPtr[] NativeArrayToIntPtrArray(IntPtr InNativeArray, int InLength)
    {
        try
        {
            if (InNativeArray == IntPtr.Zero || InLength == 0)
                return [];

            IntPtr[] result = new IntPtr[InLength];

            for (int i = 0; i < InLength; i++)
                result[i] = Marshal.ReadIntPtr(InNativeArray, i * Marshal.SizeOf<nint>());

            return result;
        }
        catch (Exception ex)
        {
            ManagedHost.HandleException(ex);
            return [];
        }
    }

    public static object?[]? MarshalParameterArray(IntPtr InNativeArray, int InLength, MethodBase? InMethodInfo)
    {
        if (InMethodInfo == null)
            return null;

        if (InNativeArray == IntPtr.Zero || InLength == 0)
            return null;

        var parameterInfos = InMethodInfo.GetParameters();
        var parameterPointers = NativeArrayToIntPtrArray(InNativeArray, InLength);
        var result = new object?[parameterPointers.Length];

        for (int i = 0; i < parameterPointers.Length; i++)
        {
            result[i] = MarshalPointer(parameterPointers[i], parameterInfos[i].ParameterType);
        }

        return result;
    }
}
