// Copyright (c) 2025 Evangelion Manuhutu

using System;
using TestScript.Core;
using TestScript.Mathf;

namespace TestScript.Scene
{
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
        private static Dictionary<ulong, Entity> _sEntities = new Dictionary<ulong, Entity>();

        // Register an entity instance
        public static void RegisterEntity(ulong entityId, Entity entity)
        {
            _sEntities[entityId] = entity;
            Console.WriteLine($"[EntityBridge] Registered entity {entityId}");
        }

        // Unregister an entity instance
        public static void UnregisterEntity(ulong entityId)
        {
            if (_sEntities.Remove(entityId))
            {
                Console.WriteLine($"[EntityBridge] Unregistered entity {entityId}");
            }
        }

        // Create entity instance by type name using reflection
        public static void CreateEntityInstance(ulong entityId, string typeName)
        {
            try
            {
                Console.WriteLine($"[EntityBridge] Attempting to create entity instance: ID={entityId}, Type={typeName}");

                // Try to find and instantiate the type using reflection
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    Console.WriteLine($"[EntityBridge] Warning: Type '{typeName}' not found, creating base Entity");
                    var entity = new Entity(entityId);
                    RegisterEntity(entityId, entity);
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
                Entity? instance = null;
                try
                {
                    Console.WriteLine($"[EntityBridge] Trying constructor with ulong parameter...");
                    // Try constructor with ulong parameter
                    instance = Activator.CreateInstance(type,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                        null,
                        [entityId],
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
                                    field.SetValue(instance, entityId);
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
                    RegisterEntity(entityId, instance);
                    Console.WriteLine($"[EntityBridge] Created entity instance of type '{typeName}' with ID {entityId}");
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
        public static void Start(ulong entityId)
        {
            try
            {
                Console.WriteLine($"[EntityBridge] Start called for entity {entityId}");
                if (_sEntities.TryGetValue(entityId, out var entity))
                {
                    Console.WriteLine($"[EntityBridge] Entity found, calling Start() on type {entity.GetType().Name}");
                    entity.Start();
                    Console.WriteLine($"[EntityBridge] Start() completed successfully");
                }
                else
                {
                    Console.WriteLine($"[EntityBridge] Warning: Entity {entityId} not found for Start()");
                    Console.WriteLine($"[EntityBridge] Registered entities: {string.Join(", ", _sEntities.Keys)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntityBridge] ERROR in Start({entityId}): {ex.Message}");
                Console.WriteLine($"[EntityBridge] Stack trace: {ex.StackTrace}");
            }
        }

        public static void Update(ulong entityId, float deltaTime)
        {
            try
            {
                if (_sEntities.TryGetValue(entityId, out var entity))
                {
                    entity.Update(deltaTime);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntityBridge] ERROR in Update({entityId}): {ex.Message}");
            }
        }

        public static void Stop(ulong entityId)
        {
            try
            {
                Console.WriteLine($"[EntityBridge] Stop called for entity {entityId}");
                if (_sEntities.TryGetValue(entityId, out var entity))
                {
                    entity.Stop();
                }
                else
                {
                    Console.WriteLine($"[EntityBridge] Warning: Entity {entityId} not found for Stop()");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EntityBridge] ERROR in Stop({entityId}): {ex.Message}");
            }
        }

        // Clear all entities (called before shutdown)
        public static void ClearAll()
        {
            Console.WriteLine($"[EntityBridge] Clearing all entities ({_sEntities.Count} total)...");
            _sEntities.Clear();
            Console.WriteLine("[EntityBridge] All entities cleared");
        }
    }
}