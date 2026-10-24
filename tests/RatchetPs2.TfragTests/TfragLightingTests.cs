using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Sdk;

internal static class TfragLightingTests
{
    public static void Run()
    {
        foreach (var sourceNormal in new[] { Vector3.UnitZ, Vector3.Normalize(new Vector3(1, 1, 2)) })
        {
            var lightNormal = new TfragPieceVector3(sourceNormal.X, sourceNormal.Y, sourceNormal.Z);
            TfragPieceVertex Vertex(float x, float y) => new(
                new(x, y, 0), new(0, 0, 1), new(x / 4, y / 4), new(0, 0, 0, 1),
                0, new(0, 0, 0, 1), lightNormal, 1);
            var geometry = new TfragPieceGeometry(
                new(new(0, 0, 0), new(4, 4, 0), 6), [0],
                [new(0, [new(new(0, false, false), [Vertex(0, 0), Vertex(4, 0), Vertex(0, 4), Vertex(4, 4)], [0, 1, 2, 1, 3, 2])]),
                 new(1, []), new(2, [])]);
            var canonical = TfragPieceCanonicalCodec.Write(geometry);
            var piece = new StaticModelTfragPiece(0,
                new(TfragPieceCanonicalCodec.ComputeContentId(canonical), TfragPieceCanonicalCodec.FormatVersion,
                    canonical, geometry),
                new(new(0, 0, 0), new(0, 0, 0, 1), new(1, 1, 1)), [0]);
            var terrain = TfragTerrainReader.Read(StaticModelTfragConverter.BuildTerrain(GameId.UYA, [piece]));
            var export = TfragGltfExporter.Export(terrain, options: new() { LodIndex = 0 });
            using var document = JsonDocument.Parse(export.GltfBytes);
            var root = document.RootElement;
            var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var attributes = primitive.GetProperty("attributes");
            var lighting = ReadVectors(root, export.BinBytes,
                attributes.GetProperty(TfragGltfExporter.LightNormalAttributeName).GetInt32());
            var expected = new Vector3(-sourceNormal.X, -sourceNormal.Z, sourceNormal.Y);
            Expect(lighting.All(normal => Vector3.Dot(normal, expected) > 0.999f),
                "Exported tfrag lighting vectors must oppose surface normals, matching the PS2 and Map-o-Matic convention.");
            var surface = ReadVectors(root, export.BinBytes, attributes.GetProperty("NORMAL").GetInt32());
            Expect(surface.All(normal => normal.Y > 0.99f),
                "Correcting lighting vectors must preserve outward geometric normals.");
            var positions = ReadVectors(root, export.BinBytes, attributes.GetProperty("POSITION").GetInt32());
            var indices = root.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()];
            var offset = root.GetProperty("bufferViews")[indices.GetProperty("bufferView").GetInt32()]
                .GetProperty("byteOffset").GetInt32();
            var a = (int)BitConverter.ToUInt32(export.BinBytes, offset);
            var b = (int)BitConverter.ToUInt32(export.BinBytes, offset + 4);
            var c = (int)BitConverter.ToUInt32(export.BinBytes, offset + 8);
            Expect(Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).Y > 0,
                "Correcting lighting vectors must preserve front-facing terrain winding.");
        }
    }

    private static Vector3[] ReadVectors(JsonElement root, byte[] buffer, int index)
    {
        var accessor = root.GetProperty("accessors")[index];
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var offset = view.GetProperty("byteOffset").GetInt32()
            + (accessor.TryGetProperty("byteOffset", out var local) ? local.GetInt32() : 0);
        return Enumerable.Range(0, accessor.GetProperty("count").GetInt32()).Select(vertex => new Vector3(
            BitConverter.ToSingle(buffer, offset + vertex * 12),
            BitConverter.ToSingle(buffer, offset + vertex * 12 + 4),
            BitConverter.ToSingle(buffer, offset + vertex * 12 + 8))).ToArray();
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
