using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace RatchetPs2.Core.Gltf;

public static partial class StaticModelGltfImporter
{
    private static List<StaticModelPrimitive> ReadGeometry(
        JsonElement root,
        JsonElement accessors,
        IReadOnlyList<BufferView> bufferViews,
        int materialCount,
        string sourceName,
        List<StaticModelNativeDiagnostic> diagnostics,
        Func<bool>? cancellationRequested)
    {
        var meshes = RequiredArray(root, "meshes", StaticModelGltfLimits.MaximumMeshes, sourceName);
        if (meshes.GetArrayLength() == 0)
            throw new InvalidDataException($"{sourceName} contains no meshes.");
        var nodes = OptionalArray(root, "nodes", StaticModelGltfLimits.MaximumNodes, sourceName);
        var result = new List<StaticModelPrimitive>();
        long totalVertices = 0;
        long totalIndices = 0;
        var ignoredAttributes = new HashSet<(int Mesh, int Primitive, string Attribute)>();

        void AddMesh(int meshIndex, Matrix4x4 transform)
        {
            ThrowIfCancellationRequested(cancellationRequested);
            if ((uint)meshIndex >= (uint)meshes.GetArrayLength())
                throw new InvalidDataException($"{sourceName} node references missing mesh {meshIndex}.");
            var mesh = RequireObject(meshes[meshIndex], $"{sourceName} mesh {meshIndex}");
            RejectExtensions(mesh, $"{sourceName} mesh {meshIndex}");
            if (mesh.TryGetProperty("weights", out _))
                throw new InvalidDataException($"{sourceName} mesh {meshIndex} uses unsupported morph weights.");
            var primitives = RequiredArray(mesh, "primitives", StaticModelGltfLimits.MaximumPrimitives,
                $"{sourceName} mesh {meshIndex}");
            var meshName = ReadOptionalName(mesh, $"{sourceName} mesh {meshIndex}");
            for (var primitiveIndex = 0; primitiveIndex < primitives.GetArrayLength(); primitiveIndex++)
            {
                ThrowIfCancellationRequested(cancellationRequested);
                if (result.Count >= StaticModelGltfLimits.MaximumPrimitives)
                    throw new InvalidDataException($"{sourceName} node instances exceed the primitive limit.");
                var context = $"{sourceName} primitive {meshIndex}:{primitiveIndex}";
                var primitive = RequireObject(primitives[primitiveIndex], context);
                RejectExtensions(primitive, context);
                if (primitive.TryGetProperty("targets", out _))
                    throw new InvalidDataException($"{context} uses unsupported morph targets.");
                var mode = OptionalNonNegativeInt(primitive, "mode", 4, context);
                if (mode != 4)
                    throw new InvalidDataException($"{context} uses unsupported topology mode {mode}; expected TRIANGLES.");
                if (!primitive.TryGetProperty("attributes", out var attributes)
                    || attributes.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"{context} requires an attributes object.");
                foreach (var unsupported in attributes.EnumerateObject().Select(value => value.Name)
                    .Where(value => value is not ("POSITION" or "NORMAL" or "TEXCOORD_0" or "COLOR_0"))
                    .Order(StringComparer.Ordinal))
                {
                    if (ignoredAttributes.Add((meshIndex, primitiveIndex, unsupported)))
                        diagnostics.Add(new(
                            "ignored-vertex-attribute",
                            $"Vertex attribute {unsupported} is not used by static TIEs and was ignored.",
                            meshIndex,
                            primitiveIndex));
                }
                if (!attributes.TryGetProperty("POSITION", out var positionElement)
                    || !TryReadInt32(positionElement, out var positionAccessor)
                    || positionAccessor < 0)
                    throw new InvalidDataException($"{context} requires a POSITION accessor.");
                var positionInfo = GetAccessor(
                    positionAccessor, accessors, bufferViews, "VEC3", [5126], normalized: false, context);
                int indexCount;
                if (primitive.TryGetProperty("indices", out var indexElement))
                {
                    if (!TryReadInt32(indexElement, out var indexAccessor) || indexAccessor < 0)
                        throw new InvalidDataException($"{context} indices must reference an accessor.");
                    indexCount = GetAccessor(
                        indexAccessor, accessors, bufferViews, "SCALAR", [5121, 5123, 5125],
                        normalized: false, context).Count;
                }
                else indexCount = positionInfo.Count;
                if (positionInfo.Count == 0 || indexCount == 0 || indexCount % 3 != 0)
                    throw new InvalidDataException($"{context} must contain a non-empty triangle list.");
                totalVertices = checked(totalVertices + positionInfo.Count);
                totalIndices = checked(totalIndices + indexCount);
                if (totalVertices > StaticModelGltfLimits.MaximumVertices
                    || totalIndices > StaticModelGltfLimits.MaximumIndices)
                    throw new InvalidDataException($"{sourceName} instantiated geometry exceeds the import limits.");

                var positions = ReadFloatAccessor(positionInfo, 3, context);
                float[]? normals = null;
                if (attributes.TryGetProperty("NORMAL", out var normalElement))
                {
                    normals = ReadFloatAccessor(GetAttributeAccessor(normalElement, accessors, bufferViews,
                        "VEC3", [5126], false, context, "NORMAL"), 3, context);
                    RequireAttributeCount(normals.Length / 3, positionInfo.Count, context, "NORMAL");
                }
                float[]? texCoords = null;
                if (attributes.TryGetProperty("TEXCOORD_0", out var texCoordElement))
                {
                    var info = GetAttributeAccessor(texCoordElement, accessors, bufferViews,
                        "VEC2", [5121, 5123, 5126], null, context, "TEXCOORD_0");
                    if (info.ComponentType != 5126 && !info.Normalized)
                        throw new InvalidDataException($"{context} integer TEXCOORD_0 must be normalized.");
                    texCoords = ReadFloatAccessor(info, 2, context);
                    RequireAttributeCount(texCoords.Length / 2, positionInfo.Count, context, "TEXCOORD_0");
                }
                float[]? colors = null;
                if (attributes.TryGetProperty("COLOR_0", out var colorElement))
                {
                    var info = GetAttributeAccessor(colorElement, accessors, bufferViews,
                        null, [5121, 5123, 5126], null, context, "COLOR_0");
                    if (info.ComponentCount is not (3 or 4))
                        throw new InvalidDataException($"{context} COLOR_0 must use VEC3 or VEC4.");
                    if (info.ComponentType != 5126 && !info.Normalized)
                        throw new InvalidDataException($"{context} integer COLOR_0 must be normalized.");
                    var sourceColors = ReadFloatAccessor(info, info.ComponentCount, context);
                    RequireAttributeCount(sourceColors.Length / info.ComponentCount,
                        positionInfo.Count, context, "COLOR_0");
                    colors = ExpandColors(sourceColors, info.ComponentCount, context);
                }
                var indices = primitive.TryGetProperty("indices", out indexElement)
                    ? ReadIndices(GetAccessor(indexElement.GetInt32(), accessors, bufferViews, "SCALAR",
                        [5121, 5123, 5125], false, context), context)
                    : Enumerable.Range(0, positionInfo.Count).Select(value => checked((uint)value)).ToArray();
                if (indices.Any(value => value >= positionInfo.Count))
                    throw new InvalidDataException($"{context} contains an index outside POSITION.");
                int? materialIndex = null;
                if (primitive.TryGetProperty("material", out var materialElement))
                {
                    if (!TryReadInt32(materialElement, out var parsedMaterial) || parsedMaterial < 0
                        || parsedMaterial >= materialCount)
                        throw new InvalidDataException($"{context} references a missing material.");
                    materialIndex = parsedMaterial;
                }
                ApplyTransform(positions, normals, indices, transform, context);
                result.Add(new(meshIndex, primitiveIndex, meshName, materialIndex,
                    positions, normals, texCoords, colors, indices));
            }
        }

        if (nodes.GetArrayLength() == 0)
        {
            if (OptionalArray(root, "scenes", StaticModelGltfLimits.MaximumNodes, sourceName).GetArrayLength() != 0)
                throw new InvalidDataException($"{sourceName} declares scenes without nodes.");
            for (var meshIndex = 0; meshIndex < meshes.GetArrayLength(); meshIndex++)
                AddMesh(meshIndex, Matrix4x4.Identity);
        }
        else
        {
            var graph = ReadNodes(root, nodes, meshes.GetArrayLength(), sourceName);
            foreach (var rootIndex in graph.Roots)
                Visit(rootIndex, Matrix4x4.Identity, 1);

            void Visit(int nodeIndex, Matrix4x4 parent, int depth)
            {
                ThrowIfCancellationRequested(cancellationRequested);
                if (depth > StaticModelGltfLimits.MaximumNodeDepth)
                    throw new InvalidDataException($"{sourceName} node hierarchy exceeds the depth limit.");
                var node = graph.Nodes[nodeIndex];
                var world = node.Transform * parent;
                if (node.MeshIndex is { } meshIndex) AddMesh(meshIndex, world);
                foreach (var child in node.Children) Visit(child, world, depth + 1);
            }
        }
        if (result.Count == 0)
            throw new InvalidDataException($"{sourceName} active scene contains no triangle primitives.");
        return result;
    }

    private static NodeGraph ReadNodes(
        JsonElement root,
        JsonElement definitions,
        int meshCount,
        string sourceName)
    {
        var nodes = new NodeInfo[definitions.GetArrayLength()];
        var parentCounts = new int[nodes.Length];
        for (var index = 0; index < definitions.GetArrayLength(); index++)
        {
            var context = $"{sourceName} node {index}";
            var value = RequireObject(definitions[index], context);
            RejectExtensions(value, context);
            if (value.TryGetProperty("skin", out _) || value.TryGetProperty("weights", out _)
                || value.TryGetProperty("camera", out _))
                throw new InvalidDataException($"{context} uses unsupported skin, morph, or camera data.");
            int? meshIndex = null;
            if (value.TryGetProperty("mesh", out var meshElement))
            {
                if (!TryReadInt32(meshElement, out var parsed) || (uint)parsed >= (uint)meshCount)
                    throw new InvalidDataException($"{context} references a missing mesh.");
                meshIndex = parsed;
            }
            var children = new List<int>();
            if (value.TryGetProperty("children", out var childArray))
            {
                if (childArray.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"{context} children must be an array.");
                foreach (var childElement in childArray.EnumerateArray())
                {
                    if (!TryReadInt32(childElement, out var child) || (uint)child >= (uint)nodes.Length)
                        throw new InvalidDataException($"{context} references a missing child node.");
                    if (!children.Contains(child)) children.Add(child);
                    else throw new InvalidDataException($"{context} contains duplicate child node {child}.");
                    if (++parentCounts[child] > 1)
                        throw new InvalidDataException($"{sourceName} node {child} has more than one parent.");
                }
            }
            nodes[index] = new(meshIndex, children.ToArray(), ReadNodeTransform(value, context));
        }
        var state = new byte[nodes.Length];
        for (var index = 0; index < nodes.Length; index++) ValidateTree(index, 1);

        void ValidateTree(int index, int depth)
        {
            if (depth > StaticModelGltfLimits.MaximumNodeDepth)
                throw new InvalidDataException($"{sourceName} node hierarchy exceeds the depth limit.");
            if (state[index] == 1) throw new InvalidDataException($"{sourceName} node graph contains a cycle.");
            if (state[index] == 2) return;
            state[index] = 1;
            foreach (var child in nodes[index].Children) ValidateTree(child, depth + 1);
            state[index] = 2;
        }

        int[] roots;
        var scenes = OptionalArray(root, "scenes", StaticModelGltfLimits.MaximumNodes, sourceName);
        if (scenes.GetArrayLength() == 0)
        {
            roots = Enumerable.Range(0, nodes.Length).Where(index => parentCounts[index] == 0).ToArray();
        }
        else
        {
            var sceneIndex = OptionalNonNegativeInt(root, "scene", 0, sourceName);
            if ((uint)sceneIndex >= (uint)scenes.GetArrayLength())
                throw new InvalidDataException($"{sourceName} references missing scene {sceneIndex}.");
            var scene = RequireObject(scenes[sceneIndex], $"{sourceName} scene {sceneIndex}");
            RejectExtensions(scene, $"{sourceName} scene {sceneIndex}");
            var rootNodes = RequiredArray(scene, "nodes", StaticModelGltfLimits.MaximumNodes,
                $"{sourceName} scene {sceneIndex}");
            roots = new int[rootNodes.GetArrayLength()];
            for (var index = 0; index < roots.Length; index++)
            {
                if (!TryReadInt32(rootNodes[index], out roots[index])
                    || (uint)roots[index] >= (uint)nodes.Length || parentCounts[roots[index]] != 0)
                    throw new InvalidDataException($"{sourceName} scene {sceneIndex} has an invalid root node.");
            }
            if (roots.Distinct().Count() != roots.Length)
                throw new InvalidDataException($"{sourceName} scene {sceneIndex} contains duplicate root nodes.");
        }
        return new(nodes, roots);
    }

    private static Matrix4x4 ReadNodeTransform(JsonElement node, string context)
    {
        var hasMatrix = node.TryGetProperty("matrix", out var matrixElement);
        if (hasMatrix && (node.TryGetProperty("translation", out _) || node.TryGetProperty("rotation", out _)
                || node.TryGetProperty("scale", out _)))
            throw new InvalidDataException($"{context} cannot combine matrix and TRS transforms.");
        Matrix4x4 result;
        if (hasMatrix)
        {
            var values = ReadFloatArray(matrixElement, 16, $"{context} matrix");
            result = new(
                values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15]);
        }
        else
        {
            var translation = node.TryGetProperty("translation", out var translationElement)
                ? ReadFloatArray(translationElement, 3, $"{context} translation") : [0f, 0f, 0f];
            var scale = node.TryGetProperty("scale", out var scaleElement)
                ? ReadFloatArray(scaleElement, 3, $"{context} scale") : [1f, 1f, 1f];
            var rotation = node.TryGetProperty("rotation", out var rotationElement)
                ? ReadFloatArray(rotationElement, 4, $"{context} rotation") : [0f, 0f, 0f, 1f];
            var quaternion = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
            if (quaternion.LengthSquared() < 1e-12f)
                throw new InvalidDataException($"{context} rotation quaternion has zero length.");
            quaternion = Quaternion.Normalize(quaternion);
            result = Matrix4x4.CreateScale(new Vector3(scale[0], scale[1], scale[2]))
                * Matrix4x4.CreateFromQuaternion(quaternion)
                * Matrix4x4.CreateTranslation(new Vector3(translation[0], translation[1], translation[2]));
        }
        if (!Finite(result)) throw new InvalidDataException($"{context} transform must be finite.");
        return result;
    }

    private static AccessorInfo GetAttributeAccessor(
        JsonElement value,
        JsonElement accessors,
        IReadOnlyList<BufferView> bufferViews,
        string? expectedType,
        int[] componentTypes,
        bool? normalized,
        string context,
        string attribute)
    {
        if (!TryReadInt32(value, out var index) || index < 0)
            throw new InvalidDataException($"{context} {attribute} must reference an accessor.");
        return GetAccessor(index, accessors, bufferViews, expectedType, componentTypes, normalized, context);
    }

    private static AccessorInfo GetAccessor(
        int index,
        JsonElement accessors,
        IReadOnlyList<BufferView> bufferViews,
        string? expectedType,
        int[] componentTypes,
        bool? normalized,
        string context)
    {
        if ((uint)index >= (uint)accessors.GetArrayLength())
            throw new InvalidDataException($"{context} references missing accessor {index}.");
        var value = accessors[index];
        var type = RequiredStringProperty(value, "type", $"{context} accessor {index}");
        if (expectedType is not null && type != expectedType)
            throw new InvalidDataException($"{context} accessor {index} must use {expectedType}.");
        var componentType = RequiredNonNegativeInt(value, "componentType", $"{context} accessor {index}");
        if (!componentTypes.Contains(componentType))
            throw new InvalidDataException($"{context} accessor {index} has an unsupported component type.");
        var isNormalized = false;
        if (value.TryGetProperty("normalized", out var normalizedElement))
        {
            if (normalizedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException($"{context} accessor {index} normalized must be Boolean.");
            isNormalized = normalizedElement.GetBoolean();
        }
        if (normalized is not null && isNormalized != normalized)
            throw new InvalidDataException($"{context} accessor {index} normalized state is unsupported.");
        var viewIndex = RequiredNonNegativeInt(value, "bufferView", $"{context} accessor {index}");
        var view = bufferViews[viewIndex];
        var count = RequiredNonNegativeInt(value, "count", $"{context} accessor {index}");
        var offset = OptionalNonNegativeInt(value, "byteOffset", 0, $"{context} accessor {index}");
        var componentCount = ComponentCount(type);
        var componentSize = ComponentSize(componentType);
        var elementSize = checked(componentCount * componentSize);
        var stride = view.Stride == 0 ? elementSize : view.Stride;
        if ((view.Offset + offset) % componentSize != 0 || stride % componentSize != 0)
            throw new InvalidDataException($"{context} accessor {index} is not component-aligned.");
        return new(view.Buffer, checked(view.Offset + offset), stride, count,
            componentType, componentCount, isNormalized);
    }

    private static float[] ReadFloatAccessor(AccessorInfo accessor, int components, string context)
    {
        var result = new float[checked(accessor.Count * components)];
        for (var row = 0; row < accessor.Count; row++)
        {
            var offset = checked(accessor.Offset + row * accessor.Stride);
            for (var component = 0; component < components; component++)
            {
                var value = ReadComponent(accessor, offset, component);
                if (!float.IsFinite(value))
                    throw new InvalidDataException($"{context} accessor contains a non-finite component.");
                result[row * components + component] = value;
            }
        }
        return result;
    }

    private static float ReadComponent(AccessorInfo accessor, int offset, int component)
    {
        var itemOffset = checked(offset + component * ComponentSize(accessor.ComponentType));
        return accessor.ComponentType switch
        {
            5121 => accessor.Normalized
                ? accessor.Buffer[itemOffset] / 255f
                : accessor.Buffer[itemOffset],
            5123 => accessor.Normalized
                ? BinaryPrimitives.ReadUInt16LittleEndian(accessor.Buffer.AsSpan(itemOffset)) / 65535f
                : BinaryPrimitives.ReadUInt16LittleEndian(accessor.Buffer.AsSpan(itemOffset)),
            5126 => BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(accessor.Buffer.AsSpan(itemOffset))),
            _ => throw new InvalidDataException(
                $"Unsupported floating accessor component type {accessor.ComponentType}."),
        };
    }

    private static uint[] ReadIndices(AccessorInfo accessor, string context)
    {
        var result = new uint[accessor.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = checked(accessor.Offset + index * accessor.Stride);
            result[index] = accessor.ComponentType switch
            {
                5121 => accessor.Buffer[offset],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(accessor.Buffer.AsSpan(offset)),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(accessor.Buffer.AsSpan(offset)),
                _ => throw new InvalidDataException($"{context} has an unsupported index component type."),
            };
        }
        return result;
    }

    private static float[] ExpandColors(float[] values, int components, string context)
    {
        var result = new float[checked(values.Length / components * 4)];
        for (var index = 0; index < values.Length / components; index++)
        {
            for (var component = 0; component < components; component++)
            {
                var value = values[index * components + component];
                if (value is < 0 or > 1)
                    throw new InvalidDataException($"{context} COLOR_0 must contain values between zero and one.");
                result[index * 4 + component] = value;
            }
            if (components == 3) result[index * 4 + 3] = 1;
        }
        return result;
    }

    private static void RequireAttributeCount(int actual, int expected, string context, string attribute)
    {
        if (actual != expected)
            throw new InvalidDataException($"{context} {attribute} count must match POSITION count.");
    }

    private static void ApplyTransform(
        float[] positions,
        float[]? normals,
        uint[] indices,
        Matrix4x4 transform,
        string context)
    {
        for (var index = 0; index < positions.Length; index += 3)
        {
            var transformed = Vector3.Transform(
                new Vector3(positions[index], positions[index + 1], positions[index + 2]), transform);
            if (!Finite(transformed)) throw new InvalidDataException($"{context} transform produced a non-finite position.");
            positions[index] = transformed.X;
            positions[index + 1] = transformed.Y;
            positions[index + 2] = transformed.Z;
        }
        if (normals is not null)
        {
            if (!Matrix4x4.Invert(transform, out var inverse))
                throw new InvalidDataException($"{context} transform cannot transform normals because it is singular.");
            var normalTransform = Matrix4x4.Transpose(inverse);
            for (var index = 0; index < normals.Length; index += 3)
            {
                var transformed = TransformNormal(
                    new Vector3(normals[index], normals[index + 1], normals[index + 2]), normalTransform);
                if (!Finite(transformed) || transformed.LengthSquared() < 1e-12f)
                    throw new InvalidDataException($"{context} transform produced an invalid normal.");
                transformed = Vector3.Normalize(transformed);
                normals[index] = transformed.X;
                normals[index + 1] = transformed.Y;
                normals[index + 2] = transformed.Z;
            }
        }
        if (Determinant(transform) < 0)
        {
            for (var index = 0; index < indices.Length; index += 3)
                (indices[index + 1], indices[index + 2]) = (indices[index + 2], indices[index + 1]);
        }
    }

    private static StaticModelBounds CalculateBounds(IReadOnlyList<StaticModelPrimitive> primitives)
    {
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);
        foreach (var primitive in primitives)
        {
            for (var index = 0; index < primitive.Positions.Length; index += 3)
            {
                var value = new Vector3(
                    primitive.Positions[index], primitive.Positions[index + 1], primitive.Positions[index + 2]);
                minimum = Vector3.Min(minimum, value);
                maximum = Vector3.Max(maximum, value);
            }
        }
        return new(minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13)
        && float.IsFinite(value.M14) && float.IsFinite(value.M21) && float.IsFinite(value.M22)
        && float.IsFinite(value.M23) && float.IsFinite(value.M24) && float.IsFinite(value.M31)
        && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43)
        && float.IsFinite(value.M44);

    private static Vector3 TransformNormal(Vector3 value, Matrix4x4 matrix) => new(
        value.X * matrix.M11 + value.Y * matrix.M21 + value.Z * matrix.M31,
        value.X * matrix.M12 + value.Y * matrix.M22 + value.Z * matrix.M32,
        value.X * matrix.M13 + value.Y * matrix.M23 + value.Z * matrix.M33);

    private static float Determinant(Matrix4x4 value) =>
        value.M11 * (value.M22 * (value.M33 * value.M44 - value.M34 * value.M43)
            - value.M23 * (value.M32 * value.M44 - value.M34 * value.M42)
            + value.M24 * (value.M32 * value.M43 - value.M33 * value.M42))
        - value.M12 * (value.M21 * (value.M33 * value.M44 - value.M34 * value.M43)
            - value.M23 * (value.M31 * value.M44 - value.M34 * value.M41)
            + value.M24 * (value.M31 * value.M43 - value.M33 * value.M41))
        + value.M13 * (value.M21 * (value.M32 * value.M44 - value.M34 * value.M42)
            - value.M22 * (value.M31 * value.M44 - value.M34 * value.M41)
            + value.M24 * (value.M31 * value.M42 - value.M32 * value.M41))
        - value.M14 * (value.M21 * (value.M32 * value.M43 - value.M33 * value.M42)
            - value.M22 * (value.M31 * value.M43 - value.M33 * value.M41)
            + value.M23 * (value.M31 * value.M42 - value.M32 * value.M41));

    private sealed record NodeInfo(int? MeshIndex, int[] Children, Matrix4x4 Transform);

    private sealed record NodeGraph(NodeInfo[] Nodes, int[] Roots);

    private sealed record AccessorInfo(
        byte[] Buffer,
        int Offset,
        int Stride,
        int Count,
        int ComponentType,
        int ComponentCount,
        bool Normalized);
}
