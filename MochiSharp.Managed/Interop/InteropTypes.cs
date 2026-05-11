using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MochiSharp.Managed.Interop;

public sealed class NativeArrayEnumerator<T> : IEnumerator<T>
{
    private readonly T[] _elements;
    private int _index = -1;

    public NativeArrayEnumerator(T[] elements)
    {
        _elements = elements;
    }

    public bool MoveNext()
    {
        _index++;
        return _index < _elements.Length;
    }

    void IEnumerator.Reset() => _index = -1;
    void IDisposable.Dispose()
    {
        _index = -1;
        GC.SuppressFinalize(this);
    }

    object IEnumerator.Current => Current!;

    public T Current => _elements[_index];
}

[StructLayout(LayoutKind.Sequential, Size=32, Pack=8)]
public struct NativeArray<T> : IDisposable, IEnumerable<T>
{
    private IntPtr _nativeArray;
    private IntPtr _arrayHandle;
    private int _nativeLength;

    private Bool32 _isDisposed;

    public int Length => _nativeLength;

    public NativeArray(int length)
    {
        _nativeArray = Marshal.AllocHGlobal(length * Marshal.SizeOf<T>());
        _nativeLength = length;
    }

    public NativeArray([DisallowNull] T?[] values)
    {
        _nativeArray = Marshal.AllocHGlobal(values.Length * Marshal.SizeOf<T>());
        _nativeLength = values.Length;

        for (int i = 0; i < _nativeLength; ++i)
        {
            var elem = values[i];
            if (elem == null)
                continue;

            Marshal.StructureToPtr(elem, IntPtr.Add(_nativeArray, i * Marshal.SizeOf<T>()), false);
        }
    }

    public NativeArray(IntPtr nativeArray, IntPtr arrayHandle, int length)
    {
        _nativeArray = nativeArray;
        _arrayHandle = arrayHandle;
        _nativeLength = length;
    }

    public T[] ToArray()
    {
        Span<T> data = Span<T>.Empty;
        if (_nativeArray != IntPtr.Zero && _nativeLength > 0)
        {
            unsafe { data = new Span<T>(_nativeArray.ToPointer(), _nativeLength); }
        }

        return data.ToArray();
    }

    public Span<T> ToSpan()
    {
        unsafe { return new Span<T>(_nativeArray.ToPointer(), _nativeLength); }
    }

    public ReadOnlySpan<T> ToReadOnlySpan() => ToSpan();

    public void Dispose()
    {
        if (!_isDisposed && _arrayHandle != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_nativeArray);
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }

    public IEnumerator<T> GetEnumerator() => new NativeArrayEnumerator<T>(this);
    IEnumerator IEnumerable.GetEnumerator() => new NativeArrayEnumerator<T>(this);

    public T? this[int index]
    {
        get => Marshal.PtrToStructure<T>(IntPtr.Add(_nativeArray, index * Marshal.SizeOf<T>()));
        set => Marshal.StructureToPtr<T>(value!, IntPtr.Add(_nativeArray, index * Marshal.SizeOf<T>()), false);
    }

    public static NativeArray<T> Map(T[] array)
    {
        var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        return new(handle.AddrOfPinnedObject(), GCHandle.ToIntPtr(handle), array.Length);
    }

    public static void Unmap(ref NativeArray<T> array)
    {
        GCHandle.FromIntPtr(array._arrayHandle).Free();
        array._nativeArray = IntPtr.Zero;
        array._arrayHandle = IntPtr.Zero;
        array._nativeLength = 0;
    }

    public static implicit operator T[](NativeArray<T> InArray) => InArray.ToArray();
    public static implicit operator NativeArray<T>(T[] InArray) => new(InArray);

}

public static class ArrayStorage
{
    private static Dictionary<int, GCHandle> s_FieldArrays = new();

    public static bool HasFieldArray(object? target, MemberInfo? arrayMemberInfo)
    {
        if (arrayMemberInfo == null)
            return false;

        int arrayId = arrayMemberInfo.GetHashCode();
        arrayId += target != null ? target.GetHashCode() : 0;
        return s_FieldArrays.ContainsKey(arrayId);
    }

    public static GCHandle? GetFieldArray(object? target, object? value, MemberInfo? arrayMemberInfo)
    {
        if (arrayMemberInfo == null)
            return null;

        int arrayId = arrayMemberInfo.GetHashCode();
        arrayId += target != null ? target.GetHashCode() : 0;

        if (!s_FieldArrays.TryGetValue(arrayId, out var arrayHandle))
        {
            var arrayObject = value as Array;
            arrayHandle = GCHandle.Alloc(arrayObject, GCHandleType.Pinned);
            s_FieldArrays.Add(arrayId, arrayHandle);
        }

        return arrayHandle;
    }
}

[StructLayout(LayoutKind.Sequential, Size=16, Pack=8)]
public struct NativeInstance<T> : IDisposable
{
    private IntPtr _handle;
    private readonly IntPtr _unused;

    private NativeInstance(IntPtr handle)
    {
        _handle = handle;
        _unused = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            GCHandle handle = GCHandle.FromIntPtr(_handle);
#if DEBUG
            var type = handle.Target?.GetType();
            if (type is not null)
            {
                AssemblyLoader.DeregisterHandle(type.Assembly, handle);
            }
#endif
            handle.Free();
            _handle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    public T? Get()
    {
        if (_handle == IntPtr.Zero)
            return default;

        GCHandle handle = GCHandle.FromIntPtr(_handle);
        if (handle.Target is not T)
            return default;

        return (T)handle.Target;
    }

    public static implicit operator NativeInstance<T>(T instance)
    {
        return new(GCHandle.ToIntPtr(GCHandle.Alloc(instance, GCHandleType.Normal)));
    }

    public static implicit operator T?(NativeInstance<T> instance)
    {
        return instance.Get();
    }
}

[StructLayout(LayoutKind.Explicit, Size=16)]
public struct NativeString : IDisposable
{
    [FieldOffset(0)] internal IntPtr _nativeString;
    [FieldOffset(8)] private Bool32 _isDisposed;

    public void Dispose()
    {
        if (!_isDisposed)
        {
            if (_nativeString != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_nativeString);
                _nativeString = IntPtr.Zero;
            }

            _isDisposed = true;
        }

        GC.SuppressFinalize(this);
    }

    public override string? ToString() => this;
    public static NativeString Null() => new() { _nativeString = IntPtr.Zero };

    public static implicit operator NativeString(string? str) => new() { _nativeString = Marshal.StringToCoTaskMemAuto(str) };
    public static implicit operator string?(NativeString str) => Marshal.PtrToStringAuto(str._nativeString);
}

[StructLayout(LayoutKind.Explicit, Size = 4)]
public struct Bool32
{
    [FieldOffset(0)] public uint Value;
    
    public static implicit operator Bool32(bool val) => new() { Value = val ? 1u : 0u };
    public static implicit operator bool(Bool32 bool32) => bool32.Value > 0;

}

[StructLayout(LayoutKind.Explicit, Size=4)]
public struct ReflectionType
{
    [FieldOffset(0)] private readonly int _typeId;

    public int ID => _typeId;

    public ReflectionType(int typeId)
    {
        _typeId = typeId;
    }

    public static implicit operator ReflectionType(Type? type) => new(TypeInterface.s_CachedTypes.Add(type));
}
