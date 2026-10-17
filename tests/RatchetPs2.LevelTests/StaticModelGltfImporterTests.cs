using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Png;

internal static class StaticModelGltfImporterTests
{
    public static void Run()
    {
        var geometry = GeometryBuffer();
        var png = TextureConverter.EncodePng(new Rgba32Image(1, 1, [12, 34, 56, 128]));
        var gltf = JsonBytes(Document());
        var files = new[]
        {
            new StaticModelImportFile("textures/albedo.png", png),
            new StaticModelImportFile("buffers/model.bin", geometry),
        };
        var dependencies = StaticModelGltfImporter.DiscoverDependencies(gltf, "model.gltf");
        Expect(dependencies.SequenceEqual([
            new("buffers/model.bin", StaticModelDependencyKind.Buffer),
            new("textures/albedo.png", StaticModelDependencyKind.Image),
        ]), "static glTF dependencies should be explicit, normalized, and sorted");

        var imported = StaticModelGltfImporter.Import(gltf, "model.gltf", files);
        var repeated = StaticModelGltfImporter.Import(gltf, "model.gltf", files.Reverse().ToArray());
        Expect(imported.CanonicalBytes.SequenceEqual(repeated.CanonicalBytes),
            "static glTF import should be deterministic across sidecar order");
        Expect(imported.ContentSha256 == repeated.ContentSha256,
            "static glTF content ID should be deterministic");
        Expect(imported.ContentSha256 == "a053be57678ce6ca4954d1e3b0e6741aa1afff85fc62b13728301c2f0c5a26eb",
            $"static glTF canonical snapshot changed: {imported.ContentSha256}");
        Expect(imported.Provenance.Dependencies.Count == 2,
            "static glTF provenance should retain both sidecar hashes");

        var primitive = imported.Asset.Primitives.Single();
        Expect(primitive.Positions.SequenceEqual([2f, 3f, 4f, 1f, 3f, 4f, 2f, 5f, 4f]),
            "node hierarchy transforms should be baked into canonical positions");
        Expect(primitive.Indices.SequenceEqual([0u, 2u, 1u]),
            "mirrored node transforms should preserve front-face winding");
        Expect(primitive.Normals!.SequenceEqual([0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f]),
            "canonical normals should use the inverse-transpose transform");
        Expect(primitive.TexCoords!.SequenceEqual([0f, 0f, 1f, 0f, 0f, 1f]),
            "canonical texture coordinates should retain source values");
        Expect(primitive.Colors!.SequenceEqual([
            1f, 0f, 0f, 1f,
            0f, 1f, 0f, 128 / 255f,
            0f, 0f, 1f, 1f,
        ]), "normalized integer colors should convert deterministically");
        Expect(imported.Asset.Bounds == new StaticModelBounds(1, 3, 4, 2, 5, 4),
            "canonical bounds should include baked transforms");
        Expect(imported.Asset.Images.Single().Rgba32.SequenceEqual(new byte[] { 12, 34, 56, 128 }),
            "PNG images should decode to canonical RGBA32");
        Expect(imported.Asset.Materials.Single() is
            { BaseColorTextureIndex: 0, AlphaMode: "BLEND", DoubleSided: true },
            "supported material and texture semantics should be retained");

        var extraAttributeDocument = Document();
        extraAttributeDocument["meshes"]![0]!["primitives"]![0]!["attributes"]!["COLOR_1"] = 3;
        var extraAttribute = StaticModelGltfImporter.Import(
            JsonBytes(extraAttributeDocument), "extra-attribute.gltf", files);
        Expect(extraAttribute.CanonicalBytes.SequenceEqual(imported.CanonicalBytes)
            && extraAttribute.Diagnostics.Single() is
            {
                Code: "ignored-vertex-attribute",
                SourceMeshIndex: 0,
                SourcePrimitiveIndex: 0,
            }
            && extraAttribute.Diagnostics[0].Message.Contains("COLOR_1", StringComparison.Ordinal),
            "unsupported optional vertex attributes should be ignored with an actionable diagnostic");

        var blenderMaterial = Document();
        blenderMaterial["extensionsUsed"] = new JsonArray(
            "KHR_materials_specular",
            "KHR_materials_ior",
            "KHR_materials_transmission");
        blenderMaterial["materials"]![0]!["extensions"] = new JsonObject
        {
            ["KHR_materials_specular"] = new JsonObject { ["specularFactor"] = 0.5 },
            ["KHR_materials_ior"] = new JsonObject { ["ior"] = 1.0 },
            ["KHR_materials_transmission"] = new JsonObject { ["transmissionFactor"] = 1.0 },
        };
        var approximated = StaticModelGltfImporter.Import(JsonBytes(blenderMaterial), "blender.gltf", files);
        Expect(imported.CanonicalBytes.SequenceEqual(approximated.CanonicalBytes),
            "optional Blender specular, IOR, and transmission extensions should use the base material approximation");

        var glbDocument = Document();
        glbDocument["buffers"]![0]!.AsObject().Remove("uri");
        glbDocument["buffers"]![0]!["byteLength"] = geometry.Length + png.Length;
        glbDocument["bufferViews"]!.AsArray().Add(new JsonObject
        {
            ["buffer"] = 0,
            ["byteOffset"] = geometry.Length,
            ["byteLength"] = png.Length,
        });
        glbDocument["images"]![0] = new JsonObject
        {
            ["name"] = "Albedo",
            ["bufferView"] = 5,
            ["mimeType"] = "image/png",
        };
        var glb = Glb(JsonBytes(glbDocument), geometry.Concat(png).ToArray());
        Expect(StaticModelGltfImporter.DiscoverDependencies(glb, "model.glb").Count == 0,
            "embedded GLB input should not report sidecars");
        var embedded = StaticModelGltfImporter.Import(glb, "model.glb", []);
        Expect(imported.CanonicalBytes.SequenceEqual(embedded.CanonicalBytes),
            "equivalent GLB and glTF inputs should produce identical canonical bytes");

        Reject(Change(Document(), root => root["buffers"]![0]!["uri"] = "https://example.com/model.bin"), files,
            "remote dependency URI");
        Reject(Change(Document(), root => root["buffers"]![0]!["uri"] = "../model.bin"), files,
            "escaping dependency URI");
        Reject(Change(Document(), root => root["meshes"]![0]!["primitives"]![0]!["mode"] = 5), files,
            "unsupported topology");
        Reject(Change(Document(), root => root["meshes"]![0]!["primitives"]![0]!["targets"] = new JsonArray()), files,
            "morph targets");
        Reject(Change(Document(), root => root["animations"] = new JsonArray(new JsonObject())), files,
            "animations");
        Reject(Change(Document(), root => root["skins"] = new JsonArray(new JsonObject())), files,
            "skins");
        Reject(Change(Document(), root => root["extensionsUsed"] = new JsonArray("KHR_draco_mesh_compression")), files,
            "extensions");
        Reject(Change(Document(), root => root["extensionsRequired"] = new JsonArray("KHR_materials_ior")), files,
            "required extensions");
        Reject(Change(Document(), root => root["accessors"]![0]!["count"] =
                StaticModelGltfLimits.MaximumVertices + 1), files,
            "accessor allocation bound");
        Reject(Change(Document(), root => root["bufferViews"]![0]!["byteLength"] = 35), files,
            "accessor range outside bufferView");
        Reject(Change(Document(), root => root["bufferViews"]![0]!["byteStride"] = 8), files,
            "accessor stride smaller than an element");
        Reject(Change(Document(), root => root["accessors"]![3]!["normalized"] = false), files,
            "unnormalized integer vertex color");
        Reject(Change(Document(), root => root["nodes"]![1]!["children"] = new JsonArray(0)), files,
            "cyclic nodes");
        var deep = Document();
        var deepNodes = new JsonArray();
        for (var index = 0; index <= StaticModelGltfLimits.MaximumNodeDepth; index++)
        {
            var node = new JsonObject();
            if (index == StaticModelGltfLimits.MaximumNodeDepth) node["mesh"] = 0;
            else node["children"] = new JsonArray(index + 1);
            deepNodes.Add(node);
        }
        deep["nodes"] = deepNodes;
        Reject(JsonBytes(deep), files, "node depth bound");
        var oversizedPng = png.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(
            oversizedPng.AsSpan(16), StaticModelGltfLimits.MaximumImageDimension + 1);
        Reject(gltf,
            [new("textures/albedo.png", oversizedPng), new("buffers/model.bin", geometry)],
            "decoded image dimension bound");
        Reject(gltf,
            [new("textures/albedo.png", png.AsSpan(0, 24).ToArray()), new("buffers/model.bin", geometry)],
            "malformed PNG context");
        Reject(gltf, [files[0]], "missing buffer sidecar");
        Reject(gltf, [.. files, new("unused.bin", [])], "unused sidecar");

        var malformedGlb = glb.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedGlb.AsSpan(8), checked((uint)malformedGlb.Length - 1));
        Reject(malformedGlb, [], "GLB container length");
        var unalignedGlb = glb.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(unalignedGlb.AsSpan(12),
            BinaryPrimitives.ReadUInt32LittleEndian(unalignedGlb.AsSpan(12)) - 1);
        Reject(unalignedGlb, [], "GLB chunk alignment");
        Reject(Change(Document(), root => root["buffers"]![0]!["uri"] = "buffers/model\u0001.bin"), files,
            "control character in dependency URI");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ExpectThrows<OperationCanceledException>(() =>
            StaticModelGltfImporter.Import(gltf, "model.gltf", files, () => cancellation.IsCancellationRequested),
            "cancelled import");

        Console.WriteLine(
            $"Static model glTF importer tests passed ({imported.ContentSha256}).");
    }

    private static JsonObject Document() => JsonNode.Parse("""
        {
          "asset": { "version": "2.0" },
          "buffers": [
            { "uri": "buffers/model.bin", "byteLength": 114 }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 72, "byteLength": 24 },
            { "buffer": 0, "byteOffset": 96, "byteLength": 12 },
            { "buffer": 0, "byteOffset": 108, "byteLength": 6 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC2" },
            { "bufferView": 3, "componentType": 5121, "normalized": true, "count": 3, "type": "VEC4" },
            { "bufferView": 4, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ],
          "images": [
            { "name": "Albedo", "uri": "textures/albedo.png" }
          ],
          "samplers": [
            { "magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 10497 }
          ],
          "textures": [
            { "name": "Albedo", "source": 0, "sampler": 0 }
          ],
          "materials": [
            {
              "name": "Paint",
              "pbrMetallicRoughness": {
                "baseColorFactor": [0.5, 0.75, 1.0, 0.5],
                "baseColorTexture": { "index": 0 },
                "metallicFactor": 0.0,
                "roughnessFactor": 0.8
              },
              "alphaMode": "BLEND",
              "doubleSided": true
            }
          ],
          "meshes": [
            {
              "name": "Triangle",
              "primitives": [
                {
                  "attributes": {
                    "POSITION": 0,
                    "NORMAL": 1,
                    "TEXCOORD_0": 2,
                    "COLOR_0": 3
                  },
                  "indices": 4,
                  "material": 0,
                  "mode": 4
                }
              ]
            }
          ],
          "nodes": [
            { "translation": [2, 3, 4], "children": [1] },
            { "mesh": 0, "scale": [-1, 2, 1] }
          ],
          "scenes": [
            { "nodes": [0] }
          ],
          "scene": 0
        }
        """)!.AsObject();

    private static byte[] GeometryBuffer()
    {
        var data = new byte[114];
        var floats = new[]
        {
            0f, 0f, 0f,
            1f, 0f, 0f,
            0f, 1f, 0f,
            0f, 0f, 1f,
            0f, 0f, 1f,
            0f, 0f, 1f,
            0f, 0f,
            1f, 0f,
            0f, 1f,
        };
        for (var index = 0; index < floats.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(index * 4), BitConverter.SingleToInt32Bits(floats[index]));
        new byte[]
        {
            255, 0, 0, 255,
            0, 255, 0, 128,
            0, 0, 255, 255,
        }.CopyTo(data, 96);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(108), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(110), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(112), 2);
        return data;
    }

    private static byte[] Glb(byte[] json, byte[] binary)
    {
        var jsonLength = (json.Length + 3) & ~3;
        var binaryLength = (binary.Length + 3) & ~3;
        var result = new byte[12 + 8 + jsonLength + 8 + binaryLength];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)result.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), checked((uint)jsonLength));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), 0x4E4F534A);
        result.AsSpan(20, jsonLength).Fill((byte)' ');
        json.CopyTo(result.AsSpan(20));
        var binaryHeader = 20 + jsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(binaryHeader), checked((uint)binaryLength));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(binaryHeader + 4), 0x004E4942);
        binary.CopyTo(result.AsSpan(binaryHeader + 8));
        return result;
    }

    private static byte[] Change(JsonObject root, Action<JsonObject> change)
    {
        change(root);
        return JsonBytes(root);
    }

    private static byte[] JsonBytes(JsonObject root) => Encoding.UTF8.GetBytes(root.ToJsonString());

    private static void Reject(byte[] gltf, IReadOnlyList<StaticModelImportFile> files, string context) =>
        ExpectThrows<InvalidDataException>(() => StaticModelGltfImporter.Import(gltf, "model.gltf", files), context);

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectThrows<TException>(Action action, string context) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}: {context}");
    }
}
