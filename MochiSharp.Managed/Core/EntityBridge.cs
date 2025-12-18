using System;
using System.Collections.Generic;
using System.Linq;
using MochiSharp.Managed.Scene;

namespace MochiSharp.Managed.Core;

// Static bridge for C++ to call into entity instances
public static class EntityBridge
{
    private static Dictionary<ulong, Entity> _sEntities = new Dictionary<ulong, Entity>();

    public static void RegisterEntity(ulong entityId, Entity entity)
    {
        _sEntities[entityId] = entity;
        Console.WriteLine($"[EntityBridge] Registered entity {entityId}");
    }

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
