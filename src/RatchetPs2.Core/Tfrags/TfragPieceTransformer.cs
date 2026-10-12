using System.Numerics;

namespace RatchetPs2.Core.Tfrags;

public static class TfragPieceTransformer
{
    private const float ScaleTolerance = 0.00001f;

    public static void Validate(TfragPieceTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!Finite(transform.Position) || !Finite(transform.Rotation) || !Finite(transform.Scale))
            throw new InvalidDataException("TFRAG_TRANSFORM_NON_FINITE: Tfrag transform values must be finite.");
        var rotationLengthSquared = transform.Rotation.X * transform.Rotation.X
            + transform.Rotation.Y * transform.Rotation.Y
            + transform.Rotation.Z * transform.Rotation.Z
            + transform.Rotation.W * transform.Rotation.W;
        if (!float.IsFinite(rotationLengthSquared) || rotationLengthSquared <= 0)
            throw new InvalidDataException("TFRAG_TRANSFORM_ROTATION_INVALID: Tfrag rotation must be a non-empty quaternion.");
        if (transform.Scale.X == 0 || transform.Scale.Y == 0 || transform.Scale.Z == 0)
            throw new InvalidDataException("TFRAG_TRANSFORM_ZERO_SCALE: Tfrag scale cannot contain zero.");
        if (transform.Scale.X < 0 || transform.Scale.Y < 0 || transform.Scale.Z < 0)
            throw new InvalidDataException("TFRAG_TRANSFORM_MIRRORED_UNSUPPORTED: Mirrored tfrag scale is unsupported.");
        if (!Near(transform.Scale.X, transform.Scale.Y) || !Near(transform.Scale.X, transform.Scale.Z))
            throw new InvalidDataException("TFRAG_TRANSFORM_NON_UNIFORM_UNSUPPORTED: Tfrag scale must be uniform.");
    }

    public static void Validate(TfragPieceBounds bounds, TfragPieceTransform transform)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        Validate(transform);
        if (!Finite(bounds.Minimum) || !Finite(bounds.Maximum) || !float.IsFinite(bounds.Radius)
            || bounds.Minimum.X > bounds.Maximum.X
            || bounds.Minimum.Y > bounds.Maximum.Y
            || bounds.Minimum.Z > bounds.Maximum.Z
            || bounds.Radius < 0)
            throw new InvalidDataException("Canonical tfrag bounds are invalid.");
        var position = Vector(transform.Position);
        var rotation = Quaternion.Normalize(ToQuaternion(transform.Rotation));
        foreach (var x in new[] { bounds.Minimum.X, bounds.Maximum.X })
        foreach (var y in new[] { bounds.Minimum.Y, bounds.Maximum.Y })
        foreach (var z in new[] { bounds.Minimum.Z, bounds.Maximum.Z })
            _ = Piece(Vector3.Transform(new Vector3(x, y, z) * transform.Scale.X, rotation) + position);
        _ = Canonical(bounds.Radius * transform.Scale.X);
    }

    public static TfragPieceGeometry Transform(
        TfragPieceGeometry geometry,
        TfragPieceTransform transform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Validate(geometry.Bounds, transform);
        var position = Vector(transform.Position);
        var rotation = Quaternion.Normalize(ToQuaternion(transform.Rotation));
        var scale = transform.Scale.X;
        var lods = geometry.Lods.Select(lod => new TfragPieceLod(
            lod.LodIndex,
            lod.Primitives.Select(primitive =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new TfragPiecePrimitive(
                    primitive.Material,
                    primitive.Vertices.Select(vertex =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return TransformVertex(vertex, position, rotation, scale);
                    }).ToArray(),
                    primitive.Indices.ToArray());
            }).ToArray())).ToArray();
        var positions = lods.SelectMany(lod => lod.Primitives)
            .SelectMany(primitive => primitive.Vertices)
            .Select(vertex => vertex.Position)
            .ToArray();
        var bounds = BuildBounds(positions, position, geometry.Bounds.Radius * scale);
        return new TfragPieceGeometry(bounds, geometry.TextureIds.ToArray(), lods);
    }

    private static TfragPieceVertex TransformVertex(
        TfragPieceVertex vertex,
        Vector3 position,
        Quaternion rotation,
        float scale) => vertex with
    {
        Position = Piece(Vector3.Transform(Vector(vertex.Position) * scale, rotation) + position),
        Normal = Piece(TransformNormal(vertex.Normal, rotation)),
        LightNormal = Piece(TransformNormal(vertex.LightNormal, rotation)),
    };

    private static Vector3 TransformNormal(TfragPieceVector3 value, Quaternion rotation)
    {
        var transformed = Vector3.Transform(Vector(value), rotation);
        return transformed.LengthSquared() > 0 ? Vector3.Normalize(transformed) : Vector3.Zero;
    }

    private static TfragPieceBounds BuildBounds(
        IReadOnlyList<TfragPieceVector3> positions,
        Vector3 center,
        float radius)
    {
        if (!float.IsFinite(radius)) throw new OverflowException("Tfrag transform exceeds finite bounds.");
        if (positions.Count == 0)
        {
            var point = Piece(center);
            return new(point, point, Canonical(radius));
        }
        var minimum = positions[0];
        var maximum = positions[0];
        foreach (var value in positions.Skip(1))
        {
            minimum = new(MathF.Min(minimum.X, value.X), MathF.Min(minimum.Y, value.Y), MathF.Min(minimum.Z, value.Z));
            maximum = new(MathF.Max(maximum.X, value.X), MathF.Max(maximum.Y, value.Y), MathF.Max(maximum.Z, value.Z));
        }
        return new(minimum, maximum, Canonical(radius));
    }

    private static TfragPieceVector3 Piece(Vector3 value) => new(
        Canonical(value.X), Canonical(value.Y), Canonical(value.Z));

    private static Vector3 Vector(TfragPieceVector3 value) => new(value.X, value.Y, value.Z);

    private static Quaternion ToQuaternion(TfragPieceVector4 value) => new(value.X, value.Y, value.Z, value.W);

    private static float Canonical(float value)
    {
        if (!float.IsFinite(value)) throw new OverflowException("Tfrag transform produced a non-finite value.");
        var rounded = MathF.Round(value, 6, MidpointRounding.ToEven);
        return rounded == 0 ? 0 : rounded;
    }

    private static bool Finite(TfragPieceVector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(TfragPieceVector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool Near(float left, float right) =>
        MathF.Abs(left - right) <= ScaleTolerance * MathF.Max(1, MathF.Max(MathF.Abs(left), MathF.Abs(right)));
}
