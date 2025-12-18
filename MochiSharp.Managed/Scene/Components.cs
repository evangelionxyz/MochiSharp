using MochiSharp.Managed.Mathf;

namespace MochiSharp.Managed.Scene;

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

    public static Transform Identity
    {
        get => new(Vector3.Zero, Vector3.Zero, Vector3.One);
    }
}
