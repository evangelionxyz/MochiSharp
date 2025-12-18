using TestScript.Scene;

namespace TestScript.Core
{
    // Internal calls to C++ functions
    // NOTE: Internal calls don't work with standard CoreCLR hosting!
    // Use delegates instead
    internal static class InternalCalls
    {
        // Delegates for internal functionality
        public delegate void Entity_GetTransformDelegate(ulong entityID, out Transform transform);
        public delegate void Entity_SetTransformDelegate(ulong entityID, ref Transform transform);
        public delegate bool Entity_HasComponentDelegate(ulong entityID, string componentType);
        public delegate void LogDelegate(string message);

        // Function pointers (set from C++)
        public static Entity_GetTransformDelegate? Entity_GetTransform { get; set; }
        public static Entity_SetTransformDelegate? Entity_SetTransform { get; set; }
        public static Entity_HasComponentDelegate? Entity_HasComponent { get; set; }
        public static LogDelegate? Log { get; set; }

        // Check if initialized
        public static bool IsInitialized
        {
            get
            {
                return Entity_GetTransform != null &&
                       Entity_SetTransform != null;
            }
        }

        // Clear all delegates (called before shutdown)
        public static void ClearDelegates()
        {
            Console.WriteLine("[InternalCalls] Clearing all delegates...");
            Entity_GetTransform = null;
            Entity_SetTransform = null;
            Entity_HasComponent = null;
            Log = null;
            Console.WriteLine("[InternalCalls] All delegates cleared");
        }
    }
}
