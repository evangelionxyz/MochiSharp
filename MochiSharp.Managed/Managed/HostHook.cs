using System;
using System.Runtime.InteropServices;

namespace MochiSharp.Managed;

// Wrapper for calling back into C++
public class HostHook
{
    private readonly Bootstrap.EngineInterface _api;

    // Define the delegate signature matching the C++ function
    private delegate void LogDelegate(IntPtr message);
    private readonly LogDelegate _logNative;

    public HostHook(Bootstrap.EngineInterface api)
    {
        _api = api;
        _logNative = Marshal.GetDelegateForFunctionPointer<LogDelegate>(_api.LogMessage);
    }

    public void Log(string message)
    {
        IntPtr ptr = Marshal.StringToCoTaskMemUTF8(message);
        _logNative(ptr);
        Marshal.FreeCoTaskMem(ptr);
    }
}
