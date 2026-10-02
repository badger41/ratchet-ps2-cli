using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Collision;

namespace RatchetPs2.Sdk;

public sealed record CollisionGltfFiles(byte[] GltfBytes, byte[] BinBytes);

public sealed record CollisionGltfPalette(
    IReadOnlyList<Vector3> CollisionTypeSrgbColors,
    IReadOnlyList<Vector3> SoundTypeSrgbColors,
    Vector3 PlayerBarrierSrgbColor);

public enum CollisionPieceKind
{
    Solid = 1,
    PlayerBarrier = 2,
}

public readonly record struct CollisionPieceEdit(
    CollisionPieceKind Kind,
    int SourcePieceIndex,
    float TranslationX,
    float TranslationY,
    float TranslationZ,
    bool Remove = false);

public sealed record CollisionComposition(
    byte[] Bytes,
    bool Changed,
    IReadOnlyList<CollisionPieceEdit> EffectiveEdits);

public readonly record struct CollisionTypeCount(byte RawType, int Count);

public sealed record CollisionPieceInfo(
    CollisionPieceKind Kind,
    int SourcePieceIndex,
    int FaceCount,
    int VertexCount,
    IReadOnlyList<CollisionTypeCount> Types);

public sealed record CollisionInspection(IReadOnlyList<CollisionPieceInfo> Pieces);

public static class CollisionConverter
{
    public static CollisionInspection Inspect(byte[] collisionBytes, GameId gameId)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        return gameId switch
        {
            GameId.UYA => InspectUya(collisionBytes),
            _ => throw new NotSupportedException($"Map collision inspection does not support {gameId}."),
        };
    }

    public static CollisionComposition Compose(
        byte[] collisionBytes,
        GameId gameId,
        IReadOnlyList<CollisionPieceEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        ArgumentNullException.ThrowIfNull(edits);
        return gameId switch
        {
            GameId.UYA => ComposeUya(collisionBytes, edits),
            _ => throw new NotSupportedException($"Map collision composition does not support {gameId}."),
        };
    }

    public static CollisionGltfFiles ExportGltf(
        byte[] collisionBytes,
        GameId gameId,
        string gltfFileName = "collision.gltf",
        string? bufferFileName = null,
        bool minify = false,
        CollisionGltfPalette? palette = null)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        var export = gameId switch
        {
            GameId.UYA => UyaCollisionGltfExporter.Export(
                collisionBytes,
                gltfFileName,
                new UyaCollisionGltfExportOptions
                {
                    BufferFileName = bufferFileName,
                    Minify = minify,
                    Palette = palette is null
                        ? UyaCollisionGltfPalette.Generic
                        : new(
                            palette.CollisionTypeSrgbColors,
                            palette.SoundTypeSrgbColors,
                            palette.PlayerBarrierSrgbColor),
                }),
            _ => throw new NotSupportedException($"Map collision glTF export does not support {gameId}.")
        };
        return new(export.GltfBytes, export.BinBytes);
    }

    private static CollisionComposition ComposeUya(
        byte[] collisionBytes,
        IReadOnlyList<CollisionPieceEdit> edits)
    {
        var nativeEdits = edits.Select(edit => new UyaCollisionPieceEdit(
            ToUyaKind(edit.Kind),
            edit.SourcePieceIndex,
            ToTicks(edit.TranslationX, edit.Kind == CollisionPieceKind.Solid ? 16 : 64, "X"),
            ToTicks(edit.TranslationY, edit.Kind == CollisionPieceKind.Solid ? 16 : 64, "Y"),
            ToTicks(edit.TranslationZ, 64, "Z"),
            edit.Remove)).ToArray();
        var composed = UyaCollisionComposer.Compose(collisionBytes, nativeEdits);
        return new(composed.Bytes, composed.Changed, composed.EffectiveEdits.Select(edit => new CollisionPieceEdit(
            FromUyaKind(edit.Kind),
            edit.SourcePieceIndex,
            edit.TranslationX64 / 64f,
            edit.TranslationY64 / 64f,
            edit.TranslationZ64 / 64f,
            edit.Remove)).ToArray());
    }

    private static CollisionInspection InspectUya(byte[] collisionBytes)
    {
        var collision = UyaCollisionReader.Read(collisionBytes);
        return new(collision.SolidPieces.Select(piece => new CollisionPieceInfo(
                CollisionPieceKind.Solid,
                piece.SourceIndex,
                piece.Faces.Count,
                CountVertices(piece),
                piece.Faces.GroupBy(face => face.Type).OrderBy(group => group.Key)
                    .Select(group => new CollisionTypeCount(group.Key, group.Count())).ToArray()))
            .Concat(collision.PlayerBarriers.Select(piece => new CollisionPieceInfo(
                CollisionPieceKind.PlayerBarrier,
                piece.SourceIndex,
                piece.Triangles.Count,
                piece.Vertices.Count,
                [])))
            .ToArray());
    }

    private static int ToTicks(float value, int precision, string axis)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Collision translation {axis} must be finite.");
        }

        return checked((int)MathF.Round(value * precision) * (64 / precision));
    }

    private static int CountVertices(UyaCollisionSolidPiece piece)
    {
        var vertices = new HashSet<UyaCollisionVertex>();
        foreach (var face in piece.Faces)
        {
            vertices.Add(face.A);
            vertices.Add(face.B);
            vertices.Add(face.C);
            if (face.IsQuad) vertices.Add(face.D);
        }
        return vertices.Count;
    }

    private static UyaCollisionPieceKind ToUyaKind(CollisionPieceKind kind) => kind switch
    {
        CollisionPieceKind.Solid => UyaCollisionPieceKind.Solid,
        CollisionPieceKind.PlayerBarrier => UyaCollisionPieceKind.PlayerBarrier,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown collision piece kind."),
    };

    private static CollisionPieceKind FromUyaKind(UyaCollisionPieceKind kind) => kind switch
    {
        UyaCollisionPieceKind.Solid => CollisionPieceKind.Solid,
        UyaCollisionPieceKind.PlayerBarrier => CollisionPieceKind.PlayerBarrier,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown UYA collision piece kind."),
    };
}
