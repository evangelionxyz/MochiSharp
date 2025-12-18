using System;
using MochiSharp.Managed.Mathf;
using MochiSharp.Managed.Scene;
using MochiSharp.Managed.Core;

namespace TestScript.Scene;

public class PlayerController : ScriptableEntity
{
    private float speed = 1.0f;
    private float time = 0.0f;

    public PlayerController(ulong id) : base(id) { }
    private PlayerController() : base(0) { }

    public override void Start()
    {
        Debug.Log("PlayerController started!");
        
        // Get initial transform
        var transform = Transform;
        Debug.Log($"Initial Position: {transform.Position}");
    }

    public override void Update(float deltaTime)
    {
        time += deltaTime;
        
        // Get current transform
        var transform = Transform;
        
        // Simple movement example: move in a circle
        transform.Position = new Vector3(
            (float)Math.Cos(time * speed) * 2.0f,
            0.0f,
            (float)Math.Sin(time * speed) * 2.0f
        );
        
        // Update transform back to C++
        Transform = transform;
    }

    public override void Stop()
    {
        Debug.Log("PlayerController stopped!");
        var transform = Transform;
        Debug.Log($"Final Position: {transform.Position}");
    }
}

// Example of a simpler script
public class RotatingEntity : ScriptableEntity
{
    private float rotationSpeed = 45.0f; // degrees per second

    // Constructor for reflection-based instantiation
    public RotatingEntity(ulong id) : base(id) { }

    // Parameterless constructor
    private RotatingEntity() : base(0) { }

    public override void Start()
    {
        Debug.Log("RotatingEntity started!");
    }

    public override void Update(float deltaTime)
    {
        var transform = Transform;
        transform.Rotation = new Vector3(
            transform.Rotation.X,
            transform.Rotation.Y + rotationSpeed * deltaTime,
            transform.Rotation.Z
        );
        Transform = transform;
    }

    public override void Stop()
    {
        Debug.Log("RotatingEntity stopped!");
    }
}
