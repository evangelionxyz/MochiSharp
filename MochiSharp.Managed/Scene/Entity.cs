using System;
using MochiSharp.Managed.Core;

namespace MochiSharp.Managed.Scene;

public class Entity
{
    public readonly ulong ID;

    public Entity(ulong id)
    {
        ID = id;
    }

    protected Entity()
    {
        ID = 0;
    }

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
            InternalCalls.Entity_SetTransform?.Invoke(ID, ref value);
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
        return instance!;
    }
}

public abstract class ScriptableEntity : Entity
{
    protected ScriptableEntity(ulong id) : base(id) { }
    protected ScriptableEntity() : base(0) { }
}
