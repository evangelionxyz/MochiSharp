// Copyright (c) 2025 Evangelion Manuhutu

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TestScript.Scene
{
    public struct Vector3
    {
        public float X, Y, Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3 Zero => new Vector3(0, 0, 0);
        public static Vector3 One => new Vector3(1, 1, 1);
        public static Vector3 Up => new Vector3(0, 1, 0);
        public static Vector3 Right => new Vector3(1, 0, 0);
        public static Vector3 Forward => new Vector3(0, 0, 1);

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 operator *(Vector3 a, float scalar) => new Vector3(a.X * scalar, a.Y * scalar, a.Z * scalar);
        public static Vector3 operator /(Vector3 a, float scalar) => new Vector3(a.X / scalar, a.Y / scalar, a.Z / scalar);

        public override string ToString() => $"Vector3({X}, {Y}, {Z})";
    }

    public struct Transform
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;

        public Transform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public static Transform Identity => new Transform(Vector3.Zero, Vector3.Zero, Vector3.One);
    }

    public class Entity
    {
        public readonly ulong ID;

        // Internal constructor for EntityBridge
        internal Entity(ulong id)
        {
            ID = id;
        }

        // Default constructor for derived classes
        protected Entity() 
        { 
            ID = 0; 
        }

        // Transform property with internal calls to C++
        public Transform Transform
        {
            get
            {
                if (InternalCalls.Entity_GetTransform != null)
                {
                    InternalCalls.Entity_GetTransform(ID, out Transform transform);
                    return transform;
                }
                // Return identity transform if not connected to C++
                return Transform.Identity;
            }
            set
            {
                if (InternalCalls.Entity_SetTransform != null)
                {
                    InternalCalls.Entity_SetTransform(ID, ref value);
                }
            }
        }

        // Lifecycle methods - to be overridden by derived classes
        public virtual void Start() { }
        public virtual void Update(float deltaTime) { }
        public virtual void Stop() { }

        // Internal factory method for EntityBridge
        internal static Entity CreateInstance(ulong id, Type type)
        {
            var instance = Activator.CreateInstance(type, true) as Entity;
            if (instance != null)
            {
                // Use reflection to set the readonly ID field
                var field = typeof(Entity).GetField(nameof(ID));
                if (field != null)
                {
                    var fieldInfo = typeof(Entity).GetField("<ID>k__BackingField", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (fieldInfo != null)
                    {
                        fieldInfo.SetValue(instance, id);
                    }
                }
                
                // Alternative: Use Unsafe or RuntimeHelpers
                // For now, we'll need derived classes to call base(id)
            }
            return instance;
        }
    }

    // Base class for user scripts
    public abstract class ScriptableEntity : Entity
    {
        // Constructor that ensures ID is set
        protected ScriptableEntity(ulong id) : base(id) { }
        
        // Default constructor for reflection
        protected ScriptableEntity() : base(0) { }

        // Helper methods for common operations
        protected void Log(string message)
        {
            Console.WriteLine($"[Entity {ID}] {message}");
        }
    }

    // Static bridge for C++ to call into entity instances
    public static class EntityBridge
    {
        private static Dictionary<ulong, Entity> s_Entities = new Dictionary<ulong, Entity>();

        // Register an entity instance
        public static void RegisterEntity(ulong entityID, Entity entity)
        {
            s_Entities[entityID] = entity;
            Console.WriteLine($"[EntityBridge] Registered entity {entityID}");
        }

        // Unregister an entity instance
        public static void UnregisterEntity(ulong entityID)
        {
            if (s_Entities.Remove(entityID))
            {
                Console.WriteLine($"[EntityBridge] Unregistered entity {entityID}");
            }
        }

        // Create entity instance by type name using reflection
        public static void CreateEntityInstance(ulong entityID, string typeName)
        {
            try
            {
                Console.WriteLine($"[EntityBridge] Attempting to create entity instance: ID={entityID}, Type={typeName}");

                // Try to find and instantiate the type using reflection
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    Console.WriteLine($"[EntityBridge] Warning: Type '{typeName}' not found, creating base Entity");
                    var entity = new Entity(entityID);
                    RegisterEntity(entityID, entity);
                    return;
                }

                Console.WriteLine($"[EntityBridge] Type found: {type.FullName}");

                // Check if type derives from Entity
                if (!typeof(Entity).IsAssignableFrom(type))
                {
                    Console.WriteLine($"[EntityBridge] Error: Type '{typeName}' does not derive from Entity");
                    return;
                }

                // Try to create instance with ID parameter first
                Entity instance = null;
                try
                {
                    Console.WriteLine($"[EntityBridge] Trying constructor with ulong parameter...");
                    // Try constructor with ulong parameter
                    instance = Activator.CreateInstance(type, 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                        null,
                        new object[] { entityID },
                        null) as Entity;
                    Console.WriteLine($"[EntityBridge] Constructor with ulong parameter succeeded!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EntityBridge] Constructor with ulong parameter failed: {ex.Message}");
                    Console.WriteLine($"[EntityBridge] Trying parameterless constructor...");
                    
                    // If that fails, try parameterless constructor and set ID via reflection
                    try
                    {
                        instance = Activator.CreateInstance(type, true) as Entity;
                        Console.WriteLine($"[EntityBridge] Parameterless constructor succeeded!");
                        
                        if (instance != null)
                        {
                            // Try to set the ID field using unsafe field access
                            var field = typeof(Entity).GetFields(
                                System.Reflection.BindingFlags.Instance | 
                                System.Reflection.BindingFlags.Public | 
                                System.Reflection.BindingFlags.NonPublic)
                                .FirstOrDefault(f => f.Name.Contains("ID") || f.Name.Contains("id"));

                            if (field != null)
                            {
                                Console.WriteLine($"[EntityBridge] Found ID field: {field.Name}, attempting to set...");
                                if (field.IsInitOnly)
                                {
                                    // For readonly fields, we need to use runtime helpers
                                    field.SetValue(instance, entityID);
                                    Console.WriteLine($"[EntityBridge] ID field set successfully");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[EntityBridge] Warning: Could not find ID field to set");
                            }
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"[EntityBridge] Parameterless constructor also failed: {innerEx.Message}");
                        throw;
                    }
                }

                if (instance != null)
                {
                    Console.WriteLine($"[EntityBridge] Instance created successfully, ID={instance.ID}");
                    RegisterEntity(entityID, instance);
                    Console.WriteLine($"[EntityBridge] Created entity instance of type '{typeName}' with ID {entityID}");
                }
                else
                {
                    Console.WriteLine($"[EntityBridge] Failed to create instance of type '{typeName}'");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntityBridge] FATAL ERROR creating entity: {ex.Message}");
                Console.WriteLine($"[EntityBridge] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[EntityBridge] Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[EntityBridge] Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        // Static methods that C++ can call - these route to instance methods
        public static void Start(ulong entityID)
        {
            try
            {
                Console.WriteLine($"[EntityBridge] Start called for entity {entityID}");
                if (s_Entities.TryGetValue(entityID, out var entity))
                {
                    Console.WriteLine($"[EntityBridge] Entity found, calling Start() on type {entity.GetType().Name}");
                    entity.Start();
                    Console.WriteLine($"[EntityBridge] Start() completed successfully");
                }
                else
                {
                    Console.WriteLine($"[EntityBridge] Warning: Entity {entityID} not found for Start()");
                    Console.WriteLine($"[EntityBridge] Registered entities: {string.Join(", ", s_Entities.Keys)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntityBridge] ERROR in Start({entityID}): {ex.Message}");
                Console.WriteLine($"[EntityBridge] Stack trace: {ex.StackTrace}");
            }
        }

        public static void Update(ulong entityID, float deltaTime)
        {
            try
            {
                if (s_Entities.TryGetValue(entityID, out var entity))
                {
                    entity.Update(deltaTime);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntityBridge] ERROR in Update({entityID}): {ex.Message}");
            }
        }

        public static void Stop(ulong entityID)
        {
            try
            {
                Console.WriteLine($"[EntityBridge] Stop called for entity {entityID}");
                if (s_Entities.TryGetValue(entityID, out var entity))
                {
                    entity.Stop();
                }
                else
                {
                    Console.WriteLine($"[EntityBridge] Warning: Entity {entityID} not found for Stop()");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntityBridge] ERROR in Stop({entityID}): {ex.Message}");
            }
        }

        // Clear all entities (called before shutdown)
        public static void ClearAll()
        {
            Console.WriteLine($"[EntityBridge] Clearing all entities ({s_Entities.Count} total)...");
            s_Entities.Clear();
            Console.WriteLine("[EntityBridge] All entities cleared");
        }
    }

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
        public static bool IsInitialized => 
            Entity_GetTransform != null && 
            Entity_SetTransform != null;

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