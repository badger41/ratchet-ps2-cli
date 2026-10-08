using RatchetPs2.Core.Shrubs;

namespace RatchetPs2.Games.UYA.Collision;

public static class UyaShrubCollisionGenerator
{
    public static UyaInstancedCollisionSurfaceCandidate GenerateSurface(
        byte[] shrubBytes,
        string additionId,
        byte rawType = 0,
        int maximumFaces = UyaInstancedCollisionSurfaceGenerator.DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        return GenerateSurface(Read(shrubBytes, cancellationToken), additionId, rawType, maximumFaces, cancellationToken);
    }

    public static UyaInstancedCollisionSurfaceCandidate GenerateSurface(
        ShrubClass shrub,
        string additionId,
        byte rawType = 0,
        int maximumFaces = UyaInstancedCollisionSurfaceGenerator.DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shrub);
        cancellationToken.ThrowIfCancellationRequested();
        var mesh = ShrubSurfaceMeshExtractor.Extract(shrub);
        Validate(mesh);
        return UyaInstancedCollisionSurfaceGenerator.GenerateSurface(
            mesh.Positions,
            mesh.Triangles.Select(value => (value.A, value.B, value.C)).ToArray(),
            additionId,
            lodIndex: 0,
            "Shrub surface",
            rawType,
            maximumFaces,
            cancellationToken);
    }

    public static UyaInstancedCollisionHullCandidate GenerateHull(
        byte[] shrubBytes,
        string additionId,
        byte rawType = 0,
        int profileSections = UyaInstancedCollisionHullGenerator.DefaultProfileSections,
        int maximumFaces = UyaInstancedCollisionHullGenerator.DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        return GenerateHull(
            Read(shrubBytes, cancellationToken), additionId, rawType, profileSections, maximumFaces, cancellationToken);
    }

    public static UyaInstancedCollisionHullCandidate GenerateHull(
        ShrubClass shrub,
        string additionId,
        byte rawType = 0,
        int profileSections = UyaInstancedCollisionHullGenerator.DefaultProfileSections,
        int maximumFaces = UyaInstancedCollisionHullGenerator.DefaultMaximumFaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shrub);
        cancellationToken.ThrowIfCancellationRequested();
        var mesh = ShrubSurfaceMeshExtractor.Extract(shrub);
        Validate(mesh);
        return UyaInstancedCollisionHullGenerator.Generate(
            mesh.Positions,
            mesh.Triangles.Select(value => (value.A, value.B, value.C)).ToArray(),
            additionId,
            lodIndex: 0,
            "Shrub surface",
            rawType,
            profileSections,
            maximumFaces,
            cancellationToken);
    }

    internal static ShrubSurfaceMesh ReadMesh(byte[] shrubBytes, CancellationToken cancellationToken)
    {
        var mesh = ShrubSurfaceMeshExtractor.Extract(Read(shrubBytes, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        Validate(mesh);
        return mesh;
    }

    private static ShrubClass Read(byte[] shrubBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shrubBytes);
        if (shrubBytes.Length == 0 || shrubBytes.Length > UyaInstancedCollisionSurfaceGenerator.MaximumSourceBytes)
            throw new ArgumentException(
                $"UYA shrub input must contain 1 through {UyaInstancedCollisionSurfaceGenerator.MaximumSourceBytes} bytes.",
                nameof(shrubBytes));
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(shrubBytes, writable: false);
        return ShrubClassReader.Read(stream);
    }

    private static void Validate(ShrubSurfaceMesh mesh)
    {
        if (mesh.Triangles.Count == 0)
            throw new InvalidDataException("Shrub contains no usable decoded surface geometry.");
    }
}
