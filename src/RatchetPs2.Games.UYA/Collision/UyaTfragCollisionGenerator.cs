using System.Numerics;
using RatchetPs2.Core.Tfrags;

namespace RatchetPs2.Games.UYA.Collision;

internal static class UyaTfragCollisionGenerator
{
    private const int SurfaceGeneratorVersion = 7;

    internal static UyaInstancedCollisionSurfaceCandidate GenerateSurface(
        byte[] canonicalBytes,
        string additionId,
        int lodIndex,
        byte rawType,
        int maximumFaces = UyaInstancedCollisionSurfaceGenerator.DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        var mesh = ReadMesh(canonicalBytes, lodIndex, cancellationToken);
        return UyaInstancedCollisionSurfaceGenerator.GenerateSurface(
            mesh.Positions,
            mesh.Triangles,
            additionId,
            lodIndex,
            $"Tfrag piece LOD {lodIndex}",
            rawType,
            maximumFaces,
            cancellationToken,
            mergeCoplanarPairs: false,
            targetFaceRatio: 1f) with { GeneratorVersion = SurfaceGeneratorVersion };
    }

    internal static UyaInstancedCollisionSurfaceCandidate GenerateDecimatedSurface(
        byte[] canonicalBytes,
        string additionId,
        byte rawType,
        float targetFaceRatio = UyaTfragCollisionSimplifier.DefaultTargetFaceRatio,
        int maximumFaces = UyaInstancedCollisionSurfaceGenerator.DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        var geometry = Read(canonicalBytes, cancellationToken);
        var lodIndices = SurfaceLodIndices(geometry);
        if (lodIndices.Length == 0)
            throw new InvalidDataException("Tfrag piece contains no usable decoded surface LOD.");
        var lodIndex = lodIndices[0];
        var mesh = ReadMesh(geometry, lodIndex, cancellationToken);
        var simplified = UyaTfragCollisionSimplifier.Simplify(
            mesh.Positions, mesh.Triangles, targetFaceRatio, cancellationToken);
        return UyaInstancedCollisionSurfaceGenerator.GenerateSurface(
            simplified.Positions,
            simplified.Triangles,
            additionId,
            lodIndex,
            $"Tfrag piece LOD {lodIndex} decimated to {targetFaceRatio:P0}",
            rawType,
            maximumFaces,
            cancellationToken,
            mergeCoplanarPairs: false,
            targetFaceRatio: targetFaceRatio) with
        {
            GeneratorVersion = SurfaceGeneratorVersion,
            SourceVertexCount = mesh.Positions.Count,
            SourceTriangleCount = mesh.Triangles.Count,
            MaximumVertexDeviation = simplified.MaximumDeviation,
        };
    }

    internal static UyaInstancedCollisionHullCandidate GenerateHull(
        byte[] canonicalBytes,
        string additionId,
        int lodIndex,
        byte rawType,
        int profileSections = UyaInstancedCollisionHullGenerator.DefaultProfileSections,
        int maximumFaces = UyaInstancedCollisionHullGenerator.DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        var mesh = ReadMesh(canonicalBytes, lodIndex, cancellationToken);
        return UyaInstancedCollisionHullGenerator.Generate(
            mesh.Positions,
            mesh.Triangles,
            additionId,
            lodIndex,
            $"Tfrag piece LOD {lodIndex}",
            rawType,
            profileSections,
            maximumFaces,
            cancellationToken);
    }

    internal static IReadOnlyList<int> InspectSurfaceLodIndices(
        byte[] canonicalBytes,
        CancellationToken cancellationToken = default) =>
        SurfaceLodIndices(Read(canonicalBytes, cancellationToken));

    private static TfragPieceGeometry Read(byte[] canonicalBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var geometry = TfragPieceCanonicalCodec.Read(canonicalBytes);
        cancellationToken.ThrowIfCancellationRequested();
        return geometry;
    }

    private static Mesh ReadMesh(
        byte[] canonicalBytes,
        int lodIndex,
        CancellationToken cancellationToken) =>
        ReadMesh(Read(canonicalBytes, cancellationToken), lodIndex, cancellationToken);

    private static Mesh ReadMesh(
        TfragPieceGeometry geometry,
        int lodIndex,
        CancellationToken cancellationToken)
    {
        var lod = geometry.Lods.SingleOrDefault(value => value.LodIndex == lodIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(lodIndex), $"Tfrag piece LOD {lodIndex} was not decoded.");
        var positions = new List<Vector3>();
        var triangles = new List<(int A, int B, int C)>();
        foreach (var primitive in lod.Primitives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = positions.Count;
            for (var index = 0; index < primitive.Vertices.Count; index++)
            {
                if ((index & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
                var position = primitive.Vertices[index].Position;
                positions.Add(new(position.X, position.Y, position.Z));
            }
            for (var index = 0; index < primitive.Indices.Count; index += 3)
            {
                if ((index & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
                var a = checked((int)primitive.Indices[index]);
                var b = checked((int)primitive.Indices[index + 1]);
                var c = checked((int)primitive.Indices[index + 2]);
                var oriented = OrientToStoredNormals(primitive, a, b, c);
                triangles.Add((
                    checked(offset + oriented.A),
                    checked(offset + oriented.B),
                    checked(offset + oriented.C)));
            }
        }
        if (triangles.Count == 0)
            throw new InvalidDataException($"Tfrag piece LOD {lodIndex} contains no usable decoded surface geometry.");
        return new(positions, triangles);
    }

    private static (int A, int B, int C) OrientToStoredNormals(
        TfragPiecePrimitive primitive,
        int a,
        int b,
        int c)
    {
        if ((uint)a >= primitive.Vertices.Count
            || (uint)b >= primitive.Vertices.Count
            || (uint)c >= primitive.Vertices.Count)
        {
            throw new InvalidDataException("Tfrag piece contains an invalid triangle vertex index.");
        }

        var vertexA = primitive.Vertices[a];
        var vertexB = primitive.Vertices[b];
        var vertexC = primitive.Vertices[c];
        var calculated = Vector3.Cross(ToVector(vertexB.Position) - ToVector(vertexA.Position),
            ToVector(vertexC.Position) - ToVector(vertexA.Position));
        var stored = ToVector(vertexA.LightNormal) + ToVector(vertexB.LightNormal) + ToVector(vertexC.LightNormal);
        return Vector3.Dot(calculated, stored) < 0 ? (c, b, a) : (a, b, c);
    }

    private static Vector3 ToVector(TfragPieceVector3 value) => new(value.X, value.Y, value.Z);

    private static int[] SurfaceLodIndices(TfragPieceGeometry geometry) => geometry.Lods
        .Where(lod => lod.Primitives.Any(primitive => primitive.Indices.Count >= 3))
        .Select(lod => lod.LodIndex)
        .Order()
        .ToArray();

    private sealed record Mesh(
        IReadOnlyList<Vector3> Positions,
        IReadOnlyList<(int A, int B, int C)> Triangles);
}
