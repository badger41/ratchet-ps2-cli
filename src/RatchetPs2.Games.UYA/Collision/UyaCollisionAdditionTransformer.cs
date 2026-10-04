using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

public static class UyaCollisionAdditionTransformer
{
    public static UyaCollisionSolidAddition Transform(
        UyaCollisionSolidAddition addition,
        string additionId,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addition);
        UyaTieCollisionGenerator.ValidateAdditionId(additionId);
        return Transform(addition, additionId, Matrix(scale, rotation, position), cancellationToken);
    }

    public static UyaCollisionSolidAddition TransformRelative(
        UyaCollisionSolidAddition addition,
        string additionId,
        UyaCollisionInstanceTransform editTransform,
        UyaCollisionInstanceTransform sourceParentTransform,
        UyaCollisionInstanceTransform targetParentTransform,
        CancellationToken cancellationToken = default)
    {
        var edit = Matrix(editTransform);
        var source = Matrix(sourceParentTransform);
        var target = Matrix(targetParentTransform);
        if (!Matrix4x4.Invert(source, out var inverseSource))
            throw new InvalidDataException("UYA collision source-parent transform is not invertible.");
        return Transform(addition, additionId, edit * inverseSource * target, cancellationToken);
    }

    private static UyaCollisionSolidAddition Transform(
        UyaCollisionSolidAddition addition,
        string additionId,
        Matrix4x4 matrix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addition);
        UyaTieCollisionGenerator.ValidateAdditionId(additionId);
        var mirrored = matrix.GetDeterminant() < 0;
        var faces = new List<UyaCollisionSolidFace>(addition.Faces.Count);
        for (var index = 0; index < addition.Faces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var face = addition.Faces[index];
            var a = TransformVertex(face.A, matrix);
            var b = TransformVertex(face.B, matrix);
            var c = TransformVertex(face.C, matrix);
            var d = face.IsQuad ? TransformVertex(face.D, matrix) : default;
            UyaCollisionSolidFace transformed = mirrored
                ? new(face.Type, a, face.IsQuad ? d : c, face.IsQuad ? c : b,
                    face.IsQuad ? b : default, face.IsQuad)
                : new(face.Type, a, b, c, d, face.IsQuad);
            if (Representable(transformed)) faces.Add(transformed);
        }
        return new(additionId, faces);
    }

    private static Matrix4x4 Matrix(UyaCollisionInstanceTransform transform) =>
        Matrix(transform.Scale, transform.Rotation, transform.Position);

    private static Matrix4x4 Matrix(Vector3 scale, Quaternion rotation, Vector3 position)
    {
        if (!Finite(position) || !Finite(rotation) || !Finite(scale))
            throw new InvalidDataException("UYA collision instance transform must contain finite values.");
        if (scale.X == 0 || scale.Y == 0 || scale.Z == 0)
            throw new InvalidDataException("UYA collision instance transform scale cannot contain zero.");
        if (rotation.LengthSquared() == 0)
            throw new InvalidDataException("UYA collision instance transform rotation cannot be empty.");
        return Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation))
            * Matrix4x4.CreateTranslation(position);
    }

    private static bool Representable(UyaCollisionSolidFace face)
    {
        if (face.A == face.B || face.A == face.C || face.B == face.C
            || face.IsQuad && (face.D == face.A || face.D == face.B || face.D == face.C))
            return false;
        var ab = face.B.Position - face.A.Position;
        var ac = face.C.Position - face.A.Position;
        if (Vector3.Cross(ab, ac).LengthSquared() <= float.Epsilon) return false;
        return !face.IsQuad
            || Vector3.Cross(ac, face.D.Position - face.A.Position).LengthSquared() > float.Epsilon;
    }

    private static UyaCollisionVertex TransformVertex(UyaCollisionVertex value, Matrix4x4 matrix) =>
        UyaTieCollisionGenerator.Quantize(Vector3.Transform(value.Position, matrix));

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
