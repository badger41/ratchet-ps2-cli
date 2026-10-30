using System.Numerics;

namespace RatchetPs2.Core.Geometry;

public readonly record struct TexturedSurfaceTriangle(
    Vector3 A, Vector3 B, Vector3 C, Vector2 UvA, Vector2 UvB, Vector2 UvC,
    int MaterialIndex, bool ClampU = false, bool ClampV = false);
