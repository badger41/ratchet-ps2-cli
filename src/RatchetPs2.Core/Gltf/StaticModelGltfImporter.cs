using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Gltf;

public static partial class StaticModelGltfImporter
{
    private static readonly HashSet<string> IgnoredMaterialExtensions =
    [
        "KHR_materials_ior",
        "KHR_materials_specular",
        "KHR_materials_transmission",
    ];

    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static IReadOnlyList<StaticModelDependency> DiscoverDependencies(
        byte[] documentBytes,
        string sourceName)
    {
        ValidateSource(documentBytes, sourceName);
        using var parsed = ReadDocument(documentBytes, sourceName);
        ValidateRoot(parsed.Json.RootElement, sourceName);
        return DiscoverDependencies(parsed.Json.RootElement, sourceName);
    }

    public static StaticModelImportResult Import(
        byte[] documentBytes,
        string sourceName,
        IReadOnlyList<StaticModelImportFile> files,
        Func<bool>? cancellationRequested = null)
    {
        ValidateSource(documentBytes, sourceName);
        ArgumentNullException.ThrowIfNull(files);
        ThrowIfCancellationRequested(cancellationRequested);
        using var parsed = ReadDocument(documentBytes, sourceName);
        var root = parsed.Json.RootElement;
        ValidateRoot(root, sourceName);
        var dependencies = DiscoverDependencies(root, sourceName);
        var supplied = ValidateFiles(files, dependencies);
        var buffers = ReadBuffers(root, parsed.BinaryChunk, supplied, sourceName, cancellationRequested);
        var bufferViews = ReadBufferViews(root, buffers, sourceName);
        var accessors = RequiredArray(root, "accessors", StaticModelGltfLimits.MaximumAccessors, sourceName);
        ValidateAccessors(accessors, bufferViews, sourceName);
        var images = ReadImages(root, bufferViews, supplied, sourceName, cancellationRequested);
        var (samplers, textures) = ReadTextures(root, images.Count, sourceName);
        var materials = ReadMaterials(root, textures.Count, sourceName);
        var diagnostics = new List<StaticModelNativeDiagnostic>();
        var primitives = ReadGeometry(
            root, accessors, bufferViews, materials.Count, sourceName, diagnostics, cancellationRequested);
        var bounds = CalculateBounds(primitives);
        var asset = new StaticModelCanonicalAsset(
            StaticModelCanonicalFormat.Version,
            primitives,
            samplers,
            textures,
            materials,
            images,
            bounds);
        var canonicalBytes = StaticModelCanonicalCodec.Write(asset);
        var provenance = new StaticModelImportProvenance(
            sourceName,
            Sha256(documentBytes),
            dependencies.Select(value => new StaticModelSourceDependency(
                    value.Uri,
                    value.Kind,
                    Sha256(supplied[value.Uri])))
                .OrderBy(value => value.Uri, StringComparer.Ordinal)
                .ThenBy(value => value.Kind)
                .ToArray());
        return new(
            asset,
            canonicalBytes,
            StaticModelCanonicalCodec.ComputeContentId(canonicalBytes),
            provenance,
            diagnostics);
    }

    private static void ValidateSource(byte[] documentBytes, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(documentBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (sourceName.Length > StaticModelGltfLimits.MaximumUriLength)
            throw new InvalidDataException("glTF source name is too long.");
        if (documentBytes.Length is 0 or > StaticModelGltfLimits.MaximumDocumentBytes)
            throw new InvalidDataException(
                $"glTF source must contain at most {StaticModelGltfLimits.MaximumDocumentBytes} bytes.");
    }

    private static ParsedDocument ReadDocument(byte[] bytes, string sourceName)
    {
        if (bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == GlbMagic)
            return ReadGlb(bytes, sourceName);
        return new(ParseJson(bytes, sourceName), null);
    }

    private static ParsedDocument ReadGlb(byte[] bytes, string sourceName)
    {
        if (bytes.Length < 20)
            throw new InvalidDataException($"{sourceName} is too short for a GLB 2.0 container.");
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        if (version != 2 || declaredLength != bytes.Length)
            throw new InvalidDataException(
                $"{sourceName} must be a GLB 2.0 file with an exact container length.");
        byte[]? json = null;
        byte[]? binary = null;
        var offset = 12;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
                throw new InvalidDataException($"{sourceName} has a truncated GLB chunk header.");
            var unsignedLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
            if (unsignedLength > int.MaxValue)
                throw new InvalidDataException($"{sourceName} has a GLB chunk that exceeds the size limit.");
            var length = (int)unsignedLength;
            if ((length & 3) != 0)
                throw new InvalidDataException($"{sourceName} has a GLB chunk without four-byte alignment.");
            var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
            offset += 8;
            if (length < 0 || length > bytes.Length - offset)
                throw new InvalidDataException($"{sourceName} has a GLB chunk outside the container.");
            var chunk = bytes.AsSpan(offset, length).ToArray();
            offset += length;
            if (type == JsonChunkType && json is null && binary is null) json = chunk;
            else if (type == BinChunkType && json is not null && binary is null) binary = chunk;
            else throw new InvalidDataException($"{sourceName} has an unsupported or out-of-order GLB chunk.");
        }
        if (json is null)
            throw new InvalidDataException($"{sourceName} has no GLB JSON chunk.");
        return new(ParseJson(json, sourceName), binary);
    }

    private static JsonDocument ParseJson(byte[] bytes, string sourceName)
    {
        try { return JsonDocument.Parse(bytes); }
        catch (Exception exception)
        {
            throw new InvalidDataException($"{sourceName} contains invalid glTF JSON: {exception.Message}", exception);
        }
    }

    private static void ValidateRoot(JsonElement root, string sourceName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("asset", out var asset)
            || asset.ValueKind != JsonValueKind.Object
            || !asset.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.String
            || version.GetString() != "2.0")
            throw new InvalidDataException($"{sourceName} must declare glTF asset version 2.0.");
        ValidateUsedExtensions(root, sourceName);
        RejectNonEmpty(root, "extensionsRequired", sourceName, "required extensions");
        RejectNonEmpty(root, "animations", sourceName, "animations");
        RejectNonEmpty(root, "skins", sourceName, "skins");
        RejectNonEmpty(root, "cameras", sourceName, "cameras");
        if (root.TryGetProperty("asset", out asset) && asset.TryGetProperty("minVersion", out var minimum)
            && (minimum.ValueKind != JsonValueKind.String || minimum.GetString() != "2.0"))
            throw new InvalidDataException($"{sourceName} declares an unsupported glTF minimum version.");
    }

    private static void ValidateUsedExtensions(JsonElement root, string sourceName)
    {
        if (!root.TryGetProperty("extensionsUsed", out var value)) return;
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{sourceName} $.extensionsUsed must be an array.");
        foreach (var extension in value.EnumerateArray())
        {
            if (extension.ValueKind != JsonValueKind.String)
                throw new InvalidDataException(
                    $"{sourceName} has a non-string extension at $.extensionsUsed.");
            var extensionName = extension.GetString()!;
            if (!IgnoredMaterialExtensions.Contains(extensionName))
                throw new InvalidDataException(
                    $"{sourceName} uses unsupported extension {extensionName} at $.extensionsUsed.");
        }
    }

    private static void RejectNonEmpty(JsonElement root, string property, string sourceName, string label)
    {
        if (root.TryGetProperty(property, out var value)
            && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 0))
            throw new InvalidDataException($"{sourceName} uses unsupported {label} at $.{property}.");
    }

    private static IReadOnlyList<StaticModelDependency> DiscoverDependencies(
        JsonElement root,
        string sourceName)
    {
        var result = new List<StaticModelDependency>();
        var buffers = OptionalArray(root, "buffers", StaticModelGltfLimits.MaximumBuffers, sourceName);
        for (var index = 0; index < buffers.GetArrayLength(); index++)
        {
            if (!buffers[index].TryGetProperty("uri", out var uri)) continue;
            var value = RequiredString(uri, $"{sourceName} buffer {index} URI");
            if (!value.StartsWith("data:", StringComparison.Ordinal))
                result.Add(new(NormalizeDependencyUri(value, $"{sourceName} buffer {index}"),
                    StaticModelDependencyKind.Buffer));
        }
        var images = OptionalArray(root, "images", StaticModelGltfLimits.MaximumImages, sourceName);
        for (var index = 0; index < images.GetArrayLength(); index++)
        {
            if (!images[index].TryGetProperty("uri", out var uri)) continue;
            var value = RequiredString(uri, $"{sourceName} image {index} URI");
            if (!value.StartsWith("data:", StringComparison.Ordinal))
                result.Add(new(NormalizeDependencyUri(value, $"{sourceName} image {index}"),
                    StaticModelDependencyKind.Image));
        }
        return result.Distinct().OrderBy(value => value.Uri, StringComparer.Ordinal)
            .ThenBy(value => value.Kind).ToArray();
    }

    private static Dictionary<string, byte[]> ValidateFiles(
        IReadOnlyList<StaticModelImportFile> files,
        IReadOnlyList<StaticModelDependency> dependencies)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in files)
        {
            if (file is null || file.Bytes is null)
                throw new InvalidDataException("glTF dependency entries and bytes are required.");
            var uri = NormalizeDependencyUri(file.Uri, "glTF dependency");
            if (file.Bytes.Length > StaticModelGltfLimits.MaximumDependencyBytes)
                throw new InvalidDataException($"glTF dependency '{uri}' exceeds the byte limit.");
            total = checked(total + file.Bytes.LongLength);
            if (total > StaticModelGltfLimits.MaximumTotalDependencyBytes)
                throw new InvalidDataException("glTF dependencies exceed the total byte limit.");
            if (!result.TryAdd(uri, file.Bytes))
                throw new InvalidDataException($"glTF dependency '{uri}' is supplied more than once.");
        }
        var required = dependencies.Select(value => value.Uri).ToHashSet(StringComparer.Ordinal);
        var missing = required.FirstOrDefault(value => !result.ContainsKey(value));
        if (missing is not null) throw new InvalidDataException($"glTF dependency '{missing}' is missing.");
        var unused = result.Keys.FirstOrDefault(value => !required.Contains(value));
        if (unused is not null) throw new InvalidDataException($"glTF dependency '{unused}' is not referenced.");
        return result;
    }

    private static List<byte[]> ReadBuffers(
        JsonElement root,
        byte[]? binaryChunk,
        IReadOnlyDictionary<string, byte[]> files,
        string sourceName,
        Func<bool>? cancellationRequested)
    {
        var definitions = RequiredArray(root, "buffers", StaticModelGltfLimits.MaximumBuffers, sourceName);
        var result = new List<byte[]>(definitions.GetArrayLength());
        for (var index = 0; index < definitions.GetArrayLength(); index++)
        {
            ThrowIfCancellationRequested(cancellationRequested);
            var definition = definitions[index];
            var expected = RequiredNonNegativeInt(definition, "byteLength", $"{sourceName} buffer {index}");
            if (expected == 0 || expected > StaticModelGltfLimits.MaximumDependencyBytes)
                throw new InvalidDataException($"{sourceName} buffer {index} exceeds the byte limit.");
            byte[] bytes;
            var allowPadding = false;
            if (definition.TryGetProperty("uri", out var uriElement))
            {
                var uri = RequiredString(uriElement, $"{sourceName} buffer {index} URI");
                bytes = uri.StartsWith("data:", StringComparison.Ordinal)
                    ? ReadDataUri(uri, "application/octet-stream", StaticModelGltfLimits.MaximumDependencyBytes,
                        $"{sourceName} buffer {index}")
                    : files[NormalizeDependencyUri(uri, $"{sourceName} buffer {index}")];
            }
            else
            {
                if (index != 0 || binaryChunk is null)
                    throw new InvalidDataException($"{sourceName} buffer {index} has no URI or GLB BIN chunk.");
                bytes = binaryChunk;
                allowPadding = true;
            }
            if (bytes.Length < expected || (!allowPadding && bytes.Length != expected)
                || (allowPadding && bytes.Length > checked((expected + 3) & ~3)))
                throw new InvalidDataException(
                    $"{sourceName} buffer {index} length does not match its declared byteLength.");
            result.Add(bytes.Length == expected ? bytes : bytes.AsSpan(0, expected).ToArray());
        }
        return result;
    }

    private static List<BufferView> ReadBufferViews(
        JsonElement root,
        IReadOnlyList<byte[]> buffers,
        string sourceName)
    {
        var definitions = RequiredArray(root, "bufferViews", StaticModelGltfLimits.MaximumBufferViews, sourceName);
        var result = new List<BufferView>(definitions.GetArrayLength());
        for (var index = 0; index < definitions.GetArrayLength(); index++)
        {
            var value = definitions[index];
            var bufferIndex = RequiredNonNegativeInt(value, "buffer", $"{sourceName} bufferView {index}");
            if ((uint)bufferIndex >= (uint)buffers.Count)
                throw new InvalidDataException($"{sourceName} bufferView {index} references missing buffer {bufferIndex}.");
            var offset = OptionalNonNegativeInt(value, "byteOffset", 0, $"{sourceName} bufferView {index}");
            var length = RequiredNonNegativeInt(value, "byteLength", $"{sourceName} bufferView {index}");
            if (length == 0)
                throw new InvalidDataException($"{sourceName} bufferView {index} must not be empty.");
            var stride = OptionalNonNegativeInt(value, "byteStride", 0, $"{sourceName} bufferView {index}");
            if (stride != 0 && (stride is < 4 or > 252 || stride % 4 != 0))
                throw new InvalidDataException($"{sourceName} bufferView {index} has invalid byteStride {stride}.");
            if ((long)offset + length > buffers[bufferIndex].Length)
                throw new InvalidDataException($"{sourceName} bufferView {index} exceeds buffer {bufferIndex}.");
            result.Add(new(buffers[bufferIndex], offset, length, stride));
        }
        return result;
    }

    private static void ValidateAccessors(
        JsonElement accessors,
        IReadOnlyList<BufferView> bufferViews,
        string sourceName)
    {
        long total = 0;
        for (var index = 0; index < accessors.GetArrayLength(); index++)
        {
            var value = accessors[index];
            if (value.TryGetProperty("sparse", out _))
                throw new InvalidDataException($"{sourceName} accessor {index} uses unsupported sparse storage.");
            var viewIndex = RequiredNonNegativeInt(value, "bufferView", $"{sourceName} accessor {index}");
            if ((uint)viewIndex >= (uint)bufferViews.Count)
                throw new InvalidDataException($"{sourceName} accessor {index} references a missing bufferView.");
            var count = RequiredNonNegativeInt(value, "count", $"{sourceName} accessor {index}");
            if (count == 0 || count > StaticModelGltfLimits.MaximumIndices)
                throw new InvalidDataException($"{sourceName} accessor {index} exceeds the element limit.");
            total = checked(total + count);
            if (total > StaticModelGltfLimits.MaximumIndices + 4L * StaticModelGltfLimits.MaximumVertices)
                throw new InvalidDataException($"{sourceName} accessors exceed the total element limit.");
            var componentType = RequiredNonNegativeInt(value, "componentType", $"{sourceName} accessor {index}");
            if (componentType is not (5120 or 5121 or 5122 or 5123 or 5125 or 5126))
                throw new InvalidDataException($"{sourceName} accessor {index} has unsupported component type {componentType}.");
            var type = RequiredStringProperty(value, "type", $"{sourceName} accessor {index}");
            if (type is not ("SCALAR" or "VEC2" or "VEC3" or "VEC4"))
                throw new InvalidDataException($"{sourceName} accessor {index} has unsupported type '{type}'.");
            var components = ComponentCount(type);
            var elementSize = checked(components * ComponentSize(componentType));
            var offset = OptionalNonNegativeInt(value, "byteOffset", 0, $"{sourceName} accessor {index}");
            var view = bufferViews[viewIndex];
            var stride = view.Stride == 0 ? elementSize : view.Stride;
            if (stride < elementSize || (count > 0 && (long)offset + (long)(count - 1) * stride + elementSize > view.Length))
                throw new InvalidDataException($"{sourceName} accessor {index} exceeds its bufferView.");
        }
    }

    private static List<StaticModelImage> ReadImages(
        JsonElement root,
        IReadOnlyList<BufferView> bufferViews,
        IReadOnlyDictionary<string, byte[]> files,
        string sourceName,
        Func<bool>? cancellationRequested)
    {
        var definitions = OptionalArray(root, "images", StaticModelGltfLimits.MaximumImages, sourceName);
        var result = new List<StaticModelImage>(definitions.GetArrayLength());
        long totalPixels = 0;
        for (var index = 0; index < definitions.GetArrayLength(); index++)
        {
            ThrowIfCancellationRequested(cancellationRequested);
            var value = definitions[index];
            byte[] encoded;
            if (value.TryGetProperty("uri", out var uriElement))
            {
                if (value.TryGetProperty("bufferView", out _))
                    throw new InvalidDataException($"{sourceName} image {index} has both URI and bufferView.");
                var uri = RequiredString(uriElement, $"{sourceName} image {index} URI");
                encoded = uri.StartsWith("data:", StringComparison.Ordinal)
                    ? ReadDataUri(uri, "image/png", StaticModelGltfLimits.MaximumDependencyBytes,
                        $"{sourceName} image {index}")
                    : files[NormalizeDependencyUri(uri, $"{sourceName} image {index}")];
            }
            else
            {
                var viewIndex = RequiredNonNegativeInt(value, "bufferView", $"{sourceName} image {index}");
                if ((uint)viewIndex >= (uint)bufferViews.Count)
                    throw new InvalidDataException($"{sourceName} image {index} references a missing bufferView.");
                var mimeType = RequiredStringProperty(value, "mimeType", $"{sourceName} image {index}");
                if (mimeType != "image/png")
                    throw new InvalidDataException($"{sourceName} image {index} uses unsupported MIME type '{mimeType}'.");
                var view = bufferViews[viewIndex];
                encoded = view.Buffer.AsSpan(view.Offset, view.Length).ToArray();
            }
            if (encoded.Length is < 24 or > StaticModelGltfLimits.MaximumDependencyBytes
                || !encoded.AsSpan(0, 8).SequenceEqual(PngSignature))
                throw new InvalidDataException($"{sourceName} image {index} is not a bounded PNG image.");
            var width = BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(20, 4));
            if (width is <= 0 or > StaticModelGltfLimits.MaximumImageDimension
                || height is <= 0 or > StaticModelGltfLimits.MaximumImageDimension
                || (long)width * height > StaticModelGltfLimits.MaximumImagePixels)
                throw new InvalidDataException($"{sourceName} image {index} dimensions exceed the import limits.");
            totalPixels = checked(totalPixels + (long)width * height);
            if (totalPixels > StaticModelGltfLimits.MaximumTotalImagePixels)
                throw new InvalidDataException($"{sourceName} images exceed the total decoded-pixel limit.");
            using var stream = new MemoryStream(encoded, writable: false);
            Rgba32Image decoded;
            try { decoded = PngTextureMetadataReader.ReadRgba32(stream); }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"{sourceName} image {index} is not a supported PNG: {exception.Message}", exception);
            }
            catch (IOException exception)
            {
                throw new InvalidDataException(
                    $"{sourceName} image {index} is not a supported PNG: {exception.Message}", exception);
            }
            result.Add(new(ReadOptionalName(value, $"{sourceName} image {index}"),
                decoded.Width, decoded.Height, decoded.PixelData));
        }
        return result;
    }

    private static (List<StaticModelSampler> Samplers, List<StaticModelTexture> Textures) ReadTextures(
        JsonElement root,
        int imageCount,
        string sourceName)
    {
        var samplerDefinitions = OptionalArray(root, "samplers", StaticModelGltfLimits.MaximumTextures, sourceName);
        var samplers = new List<StaticModelSampler>(samplerDefinitions.GetArrayLength() + 1);
        for (var index = 0; index < samplerDefinitions.GetArrayLength(); index++)
        {
            var value = samplerDefinitions[index];
            var mag = OptionalNonNegativeInt(value, "magFilter", 9729, $"{sourceName} sampler {index}");
            var min = OptionalNonNegativeInt(value, "minFilter", 9987, $"{sourceName} sampler {index}");
            var wrapS = OptionalNonNegativeInt(value, "wrapS", 10497, $"{sourceName} sampler {index}");
            var wrapT = OptionalNonNegativeInt(value, "wrapT", 10497, $"{sourceName} sampler {index}");
            if (mag is not (9728 or 9729)
                || min is not (9728 or 9729 or 9984 or 9985 or 9986 or 9987)
                || wrapS is not (33071 or 33648 or 10497)
                || wrapT is not (33071 or 33648 or 10497))
                throw new InvalidDataException($"{sourceName} sampler {index} uses an unsupported filter or wrap mode.");
            samplers.Add(new(mag, min, wrapS, wrapT));
        }
        var definitions = OptionalArray(root, "textures", StaticModelGltfLimits.MaximumTextures, sourceName);
        var textures = new List<StaticModelTexture>(definitions.GetArrayLength());
        int? defaultSamplerIndex = null;
        for (var index = 0; index < definitions.GetArrayLength(); index++)
        {
            var value = definitions[index];
            RejectExtensions(value, $"{sourceName} texture {index}");
            var imageIndex = RequiredNonNegativeInt(value, "source", $"{sourceName} texture {index}");
            if ((uint)imageIndex >= (uint)imageCount)
                throw new InvalidDataException($"{sourceName} texture {index} references missing image {imageIndex}.");
            int samplerIndex;
            if (value.TryGetProperty("sampler", out _))
            {
                samplerIndex = OptionalNonNegativeInt(value, "sampler", 0,
                    $"{sourceName} texture {index}");
            }
            else
            {
                defaultSamplerIndex ??= samplers.Count;
                if (defaultSamplerIndex == samplers.Count)
                    samplers.Add(new(9729, 9987, 10497, 10497));
                samplerIndex = defaultSamplerIndex.Value;
            }
            if ((uint)samplerIndex >= (uint)samplers.Count)
                throw new InvalidDataException($"{sourceName} texture {index} references a missing sampler.");
            textures.Add(new(ReadOptionalName(value, $"{sourceName} texture {index}"), imageIndex, samplerIndex));
        }
        return (samplers, textures);
    }

    private static List<StaticModelMaterial> ReadMaterials(JsonElement root, int textureCount, string sourceName)
    {
        var definitions = OptionalArray(root, "materials", StaticModelGltfLimits.MaximumMaterials, sourceName);
        var result = new List<StaticModelMaterial>(definitions.GetArrayLength());
        for (var index = 0; index < definitions.GetArrayLength(); index++)
        {
            var value = definitions[index];
            RejectExtensions(value, $"{sourceName} material {index}", IgnoredMaterialExtensions);
            foreach (var unsupported in new[] { "normalTexture", "occlusionTexture", "emissiveTexture" })
                if (value.TryGetProperty(unsupported, out _))
                    throw new InvalidDataException($"{sourceName} material {index} uses unsupported {unsupported}.");
            if (value.TryGetProperty("emissiveFactor", out var emissive)
                && ReadFloatArray(emissive, 3, $"{sourceName} material {index} emissiveFactor").Any(v => v != 0))
                throw new InvalidDataException($"{sourceName} material {index} uses unsupported emissive color.");
            var pbr = value.TryGetProperty("pbrMetallicRoughness", out var pbrValue)
                ? RequireObject(pbrValue, $"{sourceName} material {index} pbrMetallicRoughness")
                : default;
            if (pbr.ValueKind == JsonValueKind.Object)
            {
                RejectExtensions(pbr, $"{sourceName} material {index} pbrMetallicRoughness");
                if (pbr.TryGetProperty("metallicRoughnessTexture", out _))
                    throw new InvalidDataException(
                        $"{sourceName} material {index} uses unsupported metallicRoughnessTexture.");
            }
            var baseColor = pbr.ValueKind == JsonValueKind.Object
                && pbr.TryGetProperty("baseColorFactor", out var factor)
                    ? ReadUnitFloatArray(factor, 4, $"{sourceName} material {index} baseColorFactor")
                    : [1f, 1f, 1f, 1f];
            int? baseColorTexture = null;
            if (pbr.ValueKind == JsonValueKind.Object
                && pbr.TryGetProperty("baseColorTexture", out var textureInfo))
            {
                textureInfo = RequireObject(textureInfo, $"{sourceName} material {index} baseColorTexture");
                RejectExtensions(textureInfo, $"{sourceName} material {index} baseColorTexture");
                var textureIndex = RequiredNonNegativeInt(textureInfo, "index",
                    $"{sourceName} material {index} baseColorTexture");
                if ((uint)textureIndex >= (uint)textureCount)
                    throw new InvalidDataException($"{sourceName} material {index} references a missing texture.");
                if (OptionalNonNegativeInt(textureInfo, "texCoord", 0,
                        $"{sourceName} material {index} baseColorTexture") != 0)
                    throw new InvalidDataException($"{sourceName} material {index} uses unsupported TEXCOORD set.");
                baseColorTexture = textureIndex;
            }
            var metallic = pbr.ValueKind == JsonValueKind.Object
                ? OptionalUnitFloat(pbr, "metallicFactor", 1, $"{sourceName} material {index}") : 1;
            var roughness = pbr.ValueKind == JsonValueKind.Object
                ? OptionalUnitFloat(pbr, "roughnessFactor", 1, $"{sourceName} material {index}") : 1;
            var alphaMode = value.TryGetProperty("alphaMode", out var alpha)
                ? RequiredString(alpha, $"{sourceName} material {index} alphaMode") : "OPAQUE";
            if (alphaMode is not ("OPAQUE" or "MASK" or "BLEND"))
                throw new InvalidDataException($"{sourceName} material {index} has invalid alphaMode '{alphaMode}'.");
            var alphaCutoff = OptionalUnitFloat(value, "alphaCutoff", 0.5f, $"{sourceName} material {index}");
            var doubleSided = value.TryGetProperty("doubleSided", out var sided)
                && sided.ValueKind == JsonValueKind.True;
            if (value.TryGetProperty("doubleSided", out sided)
                && sided.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException($"{sourceName} material {index} doubleSided must be Boolean.");
            result.Add(new(ReadOptionalName(value, $"{sourceName} material {index}"), baseColor,
                baseColorTexture, metallic, roughness, alphaMode, alphaCutoff, doubleSided));
        }
        return result;
    }

    private static byte[] ReadDataUri(string uri, string mimeType, int maximumBytes, string context)
    {
        if (uri.Length > StaticModelGltfLimits.MaximumDocumentBytes)
            throw new InvalidDataException($"{context} data URI exceeds the encoded-size limit.");
        var prefix = $"data:{mimeType};base64,";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            if (mimeType == "application/octet-stream"
                && uri.StartsWith("data:application/gltf-buffer;base64,", StringComparison.OrdinalIgnoreCase))
                prefix = "data:application/gltf-buffer;base64,";
            else throw new InvalidDataException($"{context} must use a base64 {mimeType} data URI.");
        }
        var encoded = uri[prefix.Length..];
        if ((long)encoded.Length * 3 / 4 > maximumBytes)
            throw new InvalidDataException($"{context} data URI exceeds the decoded-size limit.");
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > maximumBytes)
                throw new InvalidDataException($"{context} data URI exceeds the decoded-size limit.");
            return bytes;
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"{context} contains invalid base64 data.", exception);
        }
    }

    private static string NormalizeDependencyUri(string value, string context)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > StaticModelGltfLimits.MaximumUriLength
            || value.Contains("\\") || value.Contains("?") || value.Contains("#")
            || value.StartsWith("/", StringComparison.Ordinal) || value.Contains(":")
            || value.Any(character => character is < ' ' or '\u007f'))
            throw new InvalidDataException($"{context} has an invalid local URI.");
        var decoded = DecodeLocalUri(value, context);
        var segments = decoded.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."
                || segment.Contains(":") || segment.Contains("\\")))
            throw new InvalidDataException($"{context} URI escapes its source directory.");
        return string.Join("/", segments);
    }

    private static string DecodeLocalUri(string value, string context)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                result.Append(value[index]);
                continue;
            }
            if (index + 2 >= value.Length
                || HexValue(value[index + 1]) is not { } high
                || HexValue(value[index + 2]) is not { } low)
                throw new InvalidDataException($"{context} has an invalid escaped URI.");
            var decoded = high * 16 + low;
            if (decoded is < 0x20 or > 0x7e)
                throw new InvalidDataException($"{context} uses unsupported non-ASCII URI escaping.");
            result.Append((char)decoded);
            index += 2;
        }
        return result.ToString();
    }

    private static int? HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => null,
    };

    private static JsonElement RequiredArray(
        JsonElement owner,
        string property,
        int maximumCount,
        string context)
    {
        if (!owner.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{context} requires $.{property} as an array.");
        if (value.GetArrayLength() > maximumCount)
            throw new InvalidDataException($"{context} $.{property} exceeds the count limit.");
        return value;
    }

    private static JsonElement OptionalArray(
        JsonElement owner,
        string property,
        int maximumCount,
        string context)
    {
        if (!owner.TryGetProperty(property, out var value))
        {
            using var empty = JsonDocument.Parse(new byte[] { (byte)'[', (byte)']' });
            return empty.RootElement.Clone();
        }
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximumCount)
            throw new InvalidDataException($"{context} $.{property} must be a bounded array.");
        return value;
    }

    private static JsonElement RequireObject(JsonElement value, string context)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{context} must be an object.");
        return value;
    }

    private static int RequiredNonNegativeInt(JsonElement owner, string property, string context)
    {
        if (!owner.TryGetProperty(property, out var value) || !TryReadInt32(value, out var result) || result < 0)
            throw new InvalidDataException($"{context} requires non-negative integer {property}.");
        return result;
    }

    private static int OptionalNonNegativeInt(
        JsonElement owner,
        string property,
        int fallback,
        string context)
    {
        if (!owner.TryGetProperty(property, out var value)) return fallback;
        if (!TryReadInt32(value, out var result) || result < 0)
            throw new InvalidDataException($"{context} {property} must be a non-negative integer.");
        return result;
    }

    private static string RequiredStringProperty(JsonElement owner, string property, string context)
    {
        if (!owner.TryGetProperty(property, out var value))
            throw new InvalidDataException($"{context} requires {property}.");
        return RequiredString(value, $"{context} {property}");
    }

    private static string RequiredString(JsonElement value, string context)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } result)
            throw new InvalidDataException($"{context} must be a non-empty string.");
        return result;
    }

    private static string? ReadOptionalName(JsonElement owner, string context)
    {
        if (!owner.TryGetProperty("name", out var value)) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } result
            || result.Length > StaticModelGltfLimits.MaximumUriLength)
            throw new InvalidDataException($"{context} name must be a bounded string.");
        return result;
    }

    private static float OptionalUnitFloat(JsonElement owner, string property, float fallback, string context)
    {
        if (!owner.TryGetProperty(property, out var value)) return fallback;
        if (!TryReadSingle(value, out var result) || !float.IsFinite(result) || result is < 0 or > 1)
            throw new InvalidDataException($"{context} {property} must be between zero and one.");
        return result;
    }

    private static float[] ReadUnitFloatArray(JsonElement value, int count, string context)
    {
        var result = ReadFloatArray(value, count, context);
        if (result.Any(item => item is < 0 or > 1))
            throw new InvalidDataException($"{context} components must be between zero and one.");
        return result;
    }

    private static float[] ReadFloatArray(JsonElement value, int count, string context)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count)
            throw new InvalidDataException($"{context} must contain exactly {count} numbers.");
        var result = new float[count];
        for (var index = 0; index < count; index++)
        {
            if (!TryReadSingle(value[index], out result[index]) || !float.IsFinite(result[index]))
                throw new InvalidDataException($"{context}[{index}] must be finite.");
        }
        return result;
    }

    private static void RejectExtensions(
        JsonElement value,
        string context,
        IReadOnlySet<string>? ignored = null)
    {
        if (!value.TryGetProperty("extensions", out var extensions)) return;
        if (extensions.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{context} extensions must be an object.");
        foreach (var extension in extensions.EnumerateObject())
        {
            if (ignored?.Contains(extension.Name) != true)
                throw new InvalidDataException($"{context} uses unsupported extension {extension.Name}.");
            if (extension.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{context} extension {extension.Name} must be an object.");
        }
    }

    private static int ComponentCount(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        _ => throw new InvalidDataException($"Unsupported glTF accessor type '{type}'."),
    };

    private static int ComponentSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new InvalidDataException($"Unsupported glTF component type {componentType}."),
    };

    private static bool TryReadInt32(JsonElement value, out int result)
    {
        result = 0;
        if (value.ValueKind != JsonValueKind.Number) return false;
        try { result = value.GetInt32(); return true; }
        catch (Exception) { return false; }
    }

    private static bool TryReadSingle(JsonElement value, out float result)
    {
        result = 0;
        if (value.ValueKind != JsonValueKind.Number) return false;
        try { result = value.GetSingle(); return true; }
        catch (Exception) { return false; }
    }

    private static void ThrowIfCancellationRequested(Func<bool>? cancellationRequested)
    {
        if (cancellationRequested?.Invoke() == true) throw new OperationCanceledException();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ParsedDocument(JsonDocument Json, byte[]? BinaryChunk) : IDisposable
    {
        public void Dispose() => Json.Dispose();
    }

    private sealed record BufferView(byte[] Buffer, int Offset, int Length, int Stride);
}
