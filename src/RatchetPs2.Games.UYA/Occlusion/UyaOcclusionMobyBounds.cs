using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using System.Numerics;
using RatchetPs2.Core.Moby;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionMobyBounds
{
    public static UyaOcclusionBounds Transform(MobyModel model, UyaMobyInstance instance, int source, int group)
    {
        var b = model.BoundingSphere;
        var scale = model.Scale / 1024f * instance.Scale;
        if (group is < 0 or > 1023 || source < 0 || !float.IsFinite(scale) || model.Scale <= 0 || b.Radius <= 0
            || new[]{b.X,b.Y,b.Z,b.Radius,instance.Scale,instance.Position.X,instance.Position.Y,instance.Position.Z,
                instance.Rotation.X,instance.Rotation.Y,instance.Rotation.Z}.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("Invalid experimental moby bounds.");
        // ZYX Euler convention matches the existing UYA instance projection; native sphere units remain a hypothesis.
        var rotation = Matrix4x4.CreateRotationX(instance.Rotation.X) * Matrix4x4.CreateRotationY(instance.Rotation.Y) * Matrix4x4.CreateRotationZ(instance.Rotation.Z);
        var center = Vector3.Transform(new Vector3(b.X, b.Y, b.Z) * scale, rotation) + new Vector3(instance.Position.X, instance.Position.Y, instance.Position.Z);
        var radius = b.Radius * Math.Abs(scale);
        if (!float.IsFinite(center.LengthSquared()) || radius <= 0 || radius > 100_000 || Vector3.Abs(center).X > 1_000_000
            || Vector3.Abs(center).Y > 1_000_000 || Vector3.Abs(center).Z > 1_000_000) throw new InvalidDataException("Experimental moby bounds exceed limits.");
        return new(source, group, [center.X, center.Y, center.Z], radius, [b.X, b.Y, b.Z, b.Radius], model.Scale, instance.Scale);
    }
}
