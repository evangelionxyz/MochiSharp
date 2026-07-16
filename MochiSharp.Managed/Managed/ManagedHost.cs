using MochiSharp.Managed.Interop;
using System;
using System.Runtime.InteropServices;

namespace MochiSharp.Managed;

public enum MessageLevel { Trace = 1, Info = 2, Warning = 4, Error = 8 }

public static class ManagedHost
{
    private static unsafe delegate*<NativeString, void> s_ExceptionCallback;
    private static unsafe delegate*<NativeString, MessageLevel, void> s_MessageCallback;

    [UnmanagedCallersOnly]
    private static unsafe void Initialize(delegate*<NativeString, MessageLevel, void> messageCallback, delegate*<NativeString, void> exceptionCallback)
    {
        s_ExceptionCallback = exceptionCallback;
        s_MessageCallback = messageCallback;
    }


    public static void LogMessage(string message, MessageLevel level)
    {
        unsafe
        {
            using NativeString nativeStr = message;
            s_MessageCallback(nativeStr, level);
        }
    }

    internal static void HandleException(Exception ex)
    {
        unsafe
        {
            if (s_ExceptionCallback == null)
                return;

            using NativeString nativeStr = ex.ToString();
            s_ExceptionCallback(nativeStr);
        }
    }
}
