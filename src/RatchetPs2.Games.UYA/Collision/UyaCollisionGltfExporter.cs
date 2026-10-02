using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Games.UYA.Collision;

public sealed record UyaCollisionGltfExport(byte[] GltfBytes, byte[] BinBytes);

public sealed class UyaCollisionGltfExportOptions
{
    public string? BufferFileName { get; init; }

    public UyaCollisionGltfPalette Palette { get; init; } = UyaCollisionGltfPalette.Generic;

    public float PlayerBarrierOpacity { get; init; } = 0.35f;

    public bool Minify { get; init; }
}

public static class UyaCollisionGltfExporter
{
    private const string UnlitExtension = "KHR_materials_unlit";

    public static UyaCollisionGltfExport Export(
        byte[] collisionBytes,
        string gltfFileName = "collision.gltf",
        UyaCollisionGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(collisionBytes);
        return Export(UyaCollisionReader.Read(collisionBytes), gltfFileName, options);
    }

    public static UyaCollisionGltfExport Export(
        UyaMapCollision collision,
        string gltfFileName = "collision.gltf",
        UyaCollisionGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(collision);
        options ??= new UyaCollisionGltfExportOptions();
        Validate(options);

        var bufferFileName = string.IsNullOrWhiteSpace(options.BufferFileName)
            ? $"{Path.GetFileNameWithoutExtension(gltfFileName)}.buffer.bin"
            : Path.GetFileName(options.BufferFileName);
        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var buffers = new GltfBufferWriter(writer);
        var nodes = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["name"] = "collision",
                ["children"] = new[] { 1, 2 },
                ["extras"] = new
                {
                    schema = "ratchet-ps2-collision-v1",
                    game = "UYA",
                    solidPieceCount = collision.SolidPieces.Count,
                    playerBarrierCount = collision.PlayerBarriers.Count,
                    nativeOctantCount = collision.NativeOctantCount,
                    nativeFaceCount = collision.NativeFaceCount,
                    duplicateSolidFaceCount = collision.DuplicateSolidFaceCount,
                },
            },
            new() { ["name"] = "solid_collision", ["children"] = new List<int>() },
            new() { ["name"] = "player_barriers", ["children"] = new List<int>() },
        };
        var solidChildren = (List<int>)nodes[1]["children"]!;
        var barrierChildren = (List<int>)nodes[2]["children"]!;
        var meshes = new List<Dictionary<string, object>>();

        foreach (var piece in collision.SolidPieces)
        {
            var geometry = BuildSolidGeometry(piece, options.Palette);
            var meshIndex = AddMesh(meshes, buffers, $"solid_collision_{piece.SourceIndex:0000}", geometry, 0);
            solidChildren.Add(nodes.Count);
            nodes.Add(new()
            {
                ["name"] = $"solid_collision_{piece.SourceIndex:0000}",
                ["mesh"] = meshIndex,
                ["extras"] = BuildSolidExtras(piece, geometry.Positions),
            });
        }

        foreach (var barrier in collision.PlayerBarriers)
        {
            var geometry = BuildBarrierGeometry(barrier);
            var node = new Dictionary<string, object?>
            {
                ["name"] = $"player_barrier_{barrier.SourceIndex:0000}",
                ["extras"] = BuildBarrierExtras(barrier),
            };
            if (geometry.Indices.Count > 0)
            {
                node["mesh"] = AddMesh(
                    meshes,
                    buffers,
                    $"player_barrier_{barrier.SourceIndex:0000}",
                    geometry,
                    1);
            }

            barrierChildren.Add(nodes.Count);
            nodes.Add(node);
        }

        var binBytes = binStream.ToArray();
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "RatchetPs2 UYA collision glTF exporter" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = new[] { 0 } } },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = BuildMaterials(options),
            ["buffers"] = new[] { new { uri = bufferFileName, byteLength = binBytes.Length } },
            ["bufferViews"] = buffers.BufferViews,
            ["accessors"] = buffers.Accessors,
            ["extensionsUsed"] = new[] { UnlitExtension },
            ["extras"] = new { schema = "ratchet-ps2-collision-v1", game = "UYA" },
        };
        var gltfBytes = JsonSerializer.SerializeToUtf8Bytes(
            gltf,
            new JsonSerializerOptions { WriteIndented = !options.Minify });
        return new(gltfBytes, binBytes);
    }

    private static int AddMesh(
        ICollection<Dictionary<string, object>> meshes,
        GltfBufferWriter writer,
        string name,
        Geometry geometry,
        int material)
    {
        var positionAccessor = writer.WriteVector3Accessor(geometry.Positions, includeMinMax: true);
        var colorAccessor = writer.WriteNormalizedByteVector4Accessor(geometry.Colors);
        var indexAccessor = writer.WriteUInt32IndexAccessor(geometry.Indices);
        var attributes = new Dictionary<string, int>
        {
            ["POSITION"] = positionAccessor,
            ["COLOR_0"] = colorAccessor,
        };
        if (geometry.CollisionTypes is not null && geometry.SoundTypes is not null)
        {
            attributes["_COLLISION_TYPE"] = writer.WriteByteScalarAccessor(geometry.CollisionTypes);
            attributes["_SOUND_TYPE"] = writer.WriteByteScalarAccessor(geometry.SoundTypes);
        }
        var index = meshes.Count;
        meshes.Add(new()
        {
            ["name"] = name,
            ["primitives"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["attributes"] = attributes,
                    ["indices"] = indexAccessor,
                    ["mode"] = 4,
                    ["material"] = material,
                },
            },
        });
        return index;
    }

    private static Geometry BuildSolidGeometry(
        UyaCollisionSolidPiece piece,
        UyaCollisionGltfPalette palette)
    {
        var positions = new List<Vector3>();
        var colors = new List<Vector4>();
        var collisionTypes = new List<byte>();
        var soundTypes = new List<byte>();
        var indices = new List<uint>();
        foreach (var face in piece.Faces)
        {
            var baseIndex = checked((uint)positions.Count);
            positions.Add(GltfCoordinateBasis.FromPs2Position(face.A.Position.X, face.A.Position.Y, face.A.Position.Z));
            positions.Add(GltfCoordinateBasis.FromPs2Position(face.B.Position.X, face.B.Position.Y, face.B.Position.Z));
            positions.Add(GltfCoordinateBasis.FromPs2Position(face.C.Position.X, face.C.Position.Y, face.C.Position.Z));
            if (face.IsQuad)
            {
                positions.Add(GltfCoordinateBasis.FromPs2Position(face.D.Position.X, face.D.Position.Y, face.D.Position.Z));
            }

            var color = SrgbToLinear(palette.CollisionTypeSrgbColors[face.CollisionType]);
            for (var index = 0; index < (face.IsQuad ? 4 : 3); index++)
            {
                colors.Add(new(color.X, color.Y, color.Z, 1f));
                collisionTypes.Add((byte)face.CollisionType);
                soundTypes.Add((byte)face.SoundType);
            }

            if (face.IsQuad)
            {
                indices.Add(baseIndex + 3);
                indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 1);
                indices.Add(baseIndex + 3);
                indices.Add(baseIndex + 1);
                indices.Add(baseIndex);
            }
            else
            {
                indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 1);
                indices.Add(baseIndex);
            }
        }

        return new(positions, colors, indices, collisionTypes, soundTypes);
    }

    private static Geometry BuildBarrierGeometry(UyaCollisionPlayerBarrier barrier)
    {
        var positions = barrier.Vertices
            .Select(vertex => GltfCoordinateBasis.FromPs2Position(
                vertex.Position.X,
                vertex.Position.Y,
                vertex.Position.Z))
            .ToArray();
        var colors = Enumerable.Repeat(Vector4.One, positions.Length).ToArray();
        var indices = new List<uint>(barrier.Triangles.Count * 3);
        foreach (var triangle in barrier.Triangles)
        {
            indices.Add(triangle.C);
            indices.Add(triangle.B);
            indices.Add(triangle.A);
        }

        return new(positions, colors, indices, null, null);
    }

    private static object BuildSolidExtras(
        UyaCollisionSolidPiece piece,
        IReadOnlyList<Vector3> positions) => new
        {
            schema = "ratchet-ps2-collision-v1",
            game = "UYA",
            kind = "solid",
            sourcePieceIndex = piece.SourceIndex,
            faceCount = piece.Faces.Count,
            triangleCount = piece.Faces.Count(face => !face.IsQuad),
            quadCount = piece.Faces.Count(face => face.IsQuad),
            rawTypeHistogram = piece.Faces
                .GroupBy(face => face.Type)
                .OrderBy(group => group.Key)
                .ToDictionary(group => $"0x{group.Key:X2}", group => group.Count()),
            collisionTypeHistogram = piece.Faces
                .GroupBy(face => face.CollisionType)
                .OrderBy(group => group.Key)
                .ToDictionary(group => $"0x{group.Key:X1}", group => group.Count()),
            soundTypeHistogram = piece.Faces
                .GroupBy(face => face.SoundType)
                .OrderBy(group => group.Key)
                .ToDictionary(group => $"0x{group.Key:X1}", group => group.Count()),
            bounds = Bounds(positions),
        };

    private static object BuildBarrierExtras(UyaCollisionPlayerBarrier barrier) => new
    {
        schema = "ratchet-ps2-collision-v1",
        game = "UYA",
        kind = "playerBarrier",
        sourcePieceIndex = barrier.SourceIndex,
        vertexCount = barrier.Vertices.Count,
        triangleCount = barrier.Triangles.Count,
        boundingSpherePs2 = new
        {
            center = new[]
            {
                barrier.BoundingSphere.Center.Position.X,
                barrier.BoundingSphere.Center.Position.Y,
                barrier.BoundingSphere.Center.Position.Z,
            },
            radius = barrier.BoundingSphere.Radius64 / 64f,
        },
    };

    private static object Bounds(IReadOnlyList<Vector3> positions)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return new
        {
            min = new[] { min.X, min.Y, min.Z },
            max = new[] { max.X, max.Y, max.Z },
        };
    }

    private static object[] BuildMaterials(UyaCollisionGltfExportOptions options)
    {
        var barrier = SrgbToLinear(options.Palette.PlayerBarrierSrgbColor);
        return
        [
            new Dictionary<string, object>
            {
                ["name"] = "solid_collision",
                ["pbrMetallicRoughness"] = new
                {
                    baseColorFactor = new[] { 1f, 1f, 1f, 1f },
                    metallicFactor = 0f,
                    roughnessFactor = 1f,
                },
                ["doubleSided"] = true,
                ["extensions"] = new Dictionary<string, object> { [UnlitExtension] = new { } },
            },
            new Dictionary<string, object>
            {
                ["name"] = "player_barrier",
                ["pbrMetallicRoughness"] = new
                {
                    baseColorFactor = new[] { barrier.X, barrier.Y, barrier.Z, options.PlayerBarrierOpacity },
                    metallicFactor = 0f,
                    roughnessFactor = 1f,
                },
                ["alphaMode"] = "BLEND",
                ["doubleSided"] = true,
                ["extensions"] = new Dictionary<string, object> { [UnlitExtension] = new { } },
            },
        ];
    }

    private static Vector3 SrgbToLinear(Vector3 value) => new(
        SrgbToLinear(value.X),
        SrgbToLinear(value.Y),
        SrgbToLinear(value.Z));

    private static float SrgbToLinear(float value) =>
        value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static void Validate(UyaCollisionGltfExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.Palette);
        ValidateColors(options.Palette.CollisionTypeSrgbColors, nameof(options.Palette.CollisionTypeSrgbColors));
        ValidateColors(options.Palette.SoundTypeSrgbColors, nameof(options.Palette.SoundTypeSrgbColors));
        ValidateColor(options.Palette.PlayerBarrierSrgbColor, nameof(options.Palette.PlayerBarrierSrgbColor));
        if (!float.IsFinite(options.PlayerBarrierOpacity)
            || options.PlayerBarrierOpacity <= 0f
            || options.PlayerBarrierOpacity > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(options.PlayerBarrierOpacity));
        }
    }

    private static void ValidateColors(IReadOnlyList<Vector3> colors, string name)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count != 16)
        {
            throw new ArgumentException("Collision palettes require exactly 16 colors.", name);
        }

        foreach (var color in colors)
        {
            ValidateColor(color, name);
        }
    }

    private static void ValidateColor(Vector3 color, string name)
    {
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z)
            || color.X < 0f || color.X > 1f
            || color.Y < 0f || color.Y > 1f
            || color.Z < 0f || color.Z > 1f)
        {
            throw new ArgumentOutOfRangeException(name, "Collision colors must contain finite sRGB values from 0 through 1.");
        }
    }

    private sealed record Geometry(
        IReadOnlyList<Vector3> Positions,
        IReadOnlyList<Vector4> Colors,
        IReadOnlyList<uint> Indices,
        IReadOnlyList<byte>? CollisionTypes,
        IReadOnlyList<byte>? SoundTypes);
}
