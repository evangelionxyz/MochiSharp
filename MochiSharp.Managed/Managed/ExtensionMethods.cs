using System;

namespace MochiSharp.Managed;

public static class ExtensionMethods
{
    public static bool IsDelegate(this Type type)
    {
        return typeof(MulticastDelegate).IsAssignableFrom(type.BaseType);
    }
}
