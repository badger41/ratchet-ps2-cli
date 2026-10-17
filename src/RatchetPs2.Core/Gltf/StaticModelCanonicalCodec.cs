using System.Security.Cryptography;
using System.Text;

namespace RatchetPs2.Core.Gltf;

public static class StaticModelCanonicalCodec
{
    private static ReadOnlySpan<byte> Magic => "HFSM"u8;

    public static byte[] Write(StaticModelCanonicalAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.FormatVersion != StaticModelCanonicalFormat.Version)
            throw new InvalidDataException(
                $"Static model canonical format {asset.FormatVersion} is unsupported.");
        var length = Measure(asset);
        if (length > StaticModelGltfLimits.MaximumCanonicalBytes)
            throw new InvalidDataException(
                $"Static model canonical payload exceeds {StaticModelGltfLimits.MaximumCanonicalBytes} bytes.");

        using var output = new MemoryStream(checked((int)length));
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(asset.FormatVersion);
        writer.Write(asset.Primitives.Count);
        writer.Write(asset.Samplers.Count);
        writer.Write(asset.Textures.Count);
        writer.Write(asset.Materials.Count);
        writer.Write(asset.Images.Count);
        WriteBounds(writer, asset.Bounds);
        foreach (var primitive in asset.Primitives)
        {
            writer.Write(primitive.SourceMeshIndex);
            writer.Write(primitive.SourcePrimitiveIndex);
            WriteString(writer, primitive.Name);
            writer.Write(primitive.MaterialIndex ?? -1);
            WriteFloats(writer, primitive.Positions);
            WriteNullableFloats(writer, primitive.Normals);
            WriteNullableFloats(writer, primitive.TexCoords);
            WriteNullableFloats(writer, primitive.Colors);
            writer.Write(primitive.Indices.Length);
            foreach (var index in primitive.Indices) writer.Write(index);
        }
        foreach (var sampler in asset.Samplers)
        {
            writer.Write(sampler.MagFilter);
            writer.Write(sampler.MinFilter);
            writer.Write(sampler.WrapS);
            writer.Write(sampler.WrapT);
        }
        foreach (var texture in asset.Textures)
        {
            WriteString(writer, texture.Name);
            writer.Write(texture.ImageIndex);
            writer.Write(texture.SamplerIndex);
        }
        foreach (var material in asset.Materials)
        {
            WriteString(writer, material.Name);
            WriteFloats(writer, material.BaseColorFactor);
            writer.Write(material.BaseColorTextureIndex ?? -1);
            WriteFloat(writer, material.MetallicFactor);
            WriteFloat(writer, material.RoughnessFactor);
            WriteString(writer, material.AlphaMode);
            WriteFloat(writer, material.AlphaCutoff);
            writer.Write((byte)(material.DoubleSided ? 1 : 0));
        }
        foreach (var image in asset.Images)
        {
            WriteString(writer, image.Name);
            writer.Write(image.Width);
            writer.Write(image.Height);
            writer.Write(image.Rgba32.Length);
            writer.Write(image.Rgba32);
        }
        writer.Flush();
        return output.ToArray();
    }

    public static string ComputeContentId(byte[] canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(canonicalBytes);
        return Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
    }

    private static long Measure(StaticModelCanonicalAsset asset)
    {
        long length = 4 + 6 * sizeof(int) + 6 * sizeof(float);
        foreach (var value in asset.Primitives)
        {
            length = checked(length + 3 * sizeof(int) + StringSize(value.Name)
                + FloatArraySize(value.Positions) + NullableFloatArraySize(value.Normals)
                + NullableFloatArraySize(value.TexCoords) + NullableFloatArraySize(value.Colors)
                + sizeof(int) + (long)value.Indices.Length * sizeof(uint));
        }
        length = checked(length + (long)asset.Samplers.Count * 4 * sizeof(int));
        foreach (var value in asset.Textures)
            length = checked(length + StringSize(value.Name) + 2 * sizeof(int));
        foreach (var value in asset.Materials)
        {
            length = checked(length + StringSize(value.Name) + FloatArraySize(value.BaseColorFactor)
                + sizeof(int) + 2 * sizeof(float) + StringSize(value.AlphaMode) + sizeof(float) + sizeof(byte));
        }
        foreach (var value in asset.Images)
            length = checked(length + StringSize(value.Name) + 3 * sizeof(int) + value.Rgba32.LongLength);
        return length;
    }

    private static long StringSize(string? value) =>
        sizeof(int) + (value is null ? 0 : Encoding.UTF8.GetByteCount(value));

    private static long FloatArraySize(float[] value) =>
        sizeof(int) + (long)value.Length * sizeof(float);

    private static long NullableFloatArraySize(float[]? value) =>
        sizeof(int) + (value is null ? 0 : (long)value.Length * sizeof(float));

    private static void WriteBounds(BinaryWriter writer, StaticModelBounds value)
    {
        WriteFloat(writer, value.MinimumX);
        WriteFloat(writer, value.MinimumY);
        WriteFloat(writer, value.MinimumZ);
        WriteFloat(writer, value.MaximumX);
        WriteFloat(writer, value.MaximumY);
        WriteFloat(writer, value.MaximumZ);
    }

    private static void WriteString(BinaryWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write(-1);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteFloats(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values) WriteFloat(writer, value);
    }

    private static void WriteNullableFloats(BinaryWriter writer, float[]? values)
    {
        if (values is null)
        {
            writer.Write(-1);
            return;
        }
        WriteFloats(writer, values);
    }

    private static void WriteFloat(BinaryWriter writer, float value) =>
        writer.Write(value == 0 ? 0f : value);
}
