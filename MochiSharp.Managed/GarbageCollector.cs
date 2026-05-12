using MochiSharp.Managed.Interop;
using System;
using System.Runtime.InteropServices;

namespace MochiSharp.Managed;

internal static class GarbageCollector
{
    [UnmanagedCallersOnly]
	internal static void CollectGarbage(int generation, GCCollectionMode collectionMode, Bool32 blocking, Bool32 compacting)
    {
        try
        {
            if (generation < 0)
                GC.Collect();
            else
                GC.Collect(generation, collectionMode, blocking, compacting);
        }
        catch (Exception ex)
        {
            ManagedHost.HandleException(ex);
        }
    }

    [UnmanagedCallersOnly]
    internal static void WaitForPendingFinalizers()
    {
        try
        {
            GC.WaitForPendingFinalizers();
        }
        catch (Exception ex)
        {
            ManagedHost.HandleException(ex);
        }
    }
}
