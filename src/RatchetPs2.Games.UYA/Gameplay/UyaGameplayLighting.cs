using System.Buffers.Binary;
using System.Numerics;
using static RatchetPs2.Core.IO.BinarySpanReader;
using RatchetPs2.Core.Gameplay;

namespace RatchetPs2.Games.UYA.Gameplay;

public sealed record UyaGameplayLighting(
    UyaDirectionalLight[] DirectionalLights,
    UyaPointLightTable PointLights,
    UyaEnvironmentSamplePoint[] EnvironmentSamplePoints,
    UyaEnvironmentTransition[] EnvironmentTransitions,
    byte[][] TieAmbientRgbas);

public sealed record UyaDirectionalLight(
    int Index,
    GameplayVector4 TopColor,
    GameplayVector4 TopDirection,
    GameplayVector4 InverseColor,
    GameplayVector4 InverseDirection);

public sealed record UyaDirectionalLightEdit(int SourceIndex, UyaQuaternion Rotation);

public sealed record UyaPointLightTable(
    UyaPointLight[] Lights,
    byte[] XMasks,
    byte[] YMasks,
    bool MasksMatchDerived);

public sealed record UyaPointLight(
    int Index,
    GameplayVector3 Position,
    float Radius,
    ushort PositionX,
    ushort PositionY,
    ushort PositionZ,
    ushort PackedRadius,
    ushort ColorR,
    ushort ColorG,
    ushort ColorB,
    ushort UnknownE);

public sealed record UyaPointLightEdit(int SourceIndex, GameplayVector3 Position, float Radius);

public readonly record struct UyaRgb24(byte R, byte G, byte B);
public readonly record struct UyaRgba32(byte R, byte G, byte B, byte A);

public sealed record UyaEnvironmentSamplePoint(
    int Index,
    GameplayVector3 Position,
    int HeroLight,
    short PositionX,
    short PositionY,
    short PositionZ,
    short ReverbDepth,
    short MusicTrack,
    byte FogNearIntensity,
    byte FogFarIntensity,
    UyaRgb24 HeroColor,
    byte ReverbType,
    byte ReverbDelay,
    byte ReverbFeedback,
    byte EnableReverbParameters,
    UyaRgb24 FogColor,
    short FogNearDistance,
    short FogFarDistance,
    ushort Unknown1E);

public sealed record UyaEnvironmentSamplePointEdit(int SourceIndex, GameplayVector3 Position);

public sealed record UyaEnvironmentTransition(
    int Index,
    GameplayVector4 BoundingSphere,
    float[] InverseMatrix,
    UyaRgba32 HeroColor1,
    UyaRgba32 HeroColor2,
    int HeroLight1,
    int HeroLight2,
    uint Flags,
    UyaRgba32 FogColor1,
    UyaRgba32 FogColor2,
    float FogNearDistance1,
    float FogNearIntensity1,
    float FogFarDistance1,
    float FogFarIntensity1,
    float FogNearDistance2,
    float FogNearIntensity2,
    float FogFarDistance2,
    float FogFarIntensity2,
    int Unknown7C);

public static class UyaGameplayLightingReader
{
    internal const int PointMaskSize = 64 * 16;

    public static UyaGameplayLighting Read(IReadOnlyList<GameplayRawBlock> blocks, int tieCount)
    {
        if (tieCount < 0) throw new ArgumentOutOfRangeException(nameof(tieCount));
        var directional = FindPayload(blocks, "directional_lights");
        var point = FindPayload(blocks, "point_lights");
        var samples = FindPayload(blocks, "env_sample_points");
        var transitions = FindPayload(blocks, "env_transitions");
        return new(
            directional.Length >= 0x10 ? ReadDirectionalLights(directional) : [],
            point.Length >= 0x10 ? ReadPointLights(point) : new([], [], [], true),
            samples.Length >= 0x10 ? ReadEnvironmentSamplePoints(samples) : [],
            transitions.Length >= 0x10 ? ReadEnvironmentTransitions(transitions) : [],
            ReadTieAmbientRgbas(FindPayload(blocks, "tie_ambient_rgbas"), tieCount));
    }

    public static UyaDirectionalLight[] ReadDirectionalLights(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return [];
        var count = ReadCount(data, "directional light");
        EnsureRange(data, 0x10, checked(count * 0x40), "UYA directional light records");
        var lights = new UyaDirectionalLight[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 0x10 + index * 0x40;
            lights[index] = new(index, ReadVector4(data, offset), ReadVector4(data, offset + 0x10),
                ReadVector4(data, offset + 0x20), ReadVector4(data, offset + 0x30));
        }
        return lights;
    }

    public static UyaPointLightTable ReadPointLights(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return new([], [], [], true);
        var count = ReadCount(data, "point light");
        if (count > 128) throw new InvalidDataException("UYA point light count exceeds the 128-slot mask capacity.");
        EnsureRange(data, 0x10, PointMaskSize * 2, "UYA point light masks");
        EnsureRange(data, 0x10 + PointMaskSize * 2, checked(count * 0x10), "UYA point light records");
        var lights = new UyaPointLight[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 0x10 + PointMaskSize * 2 + index * 0x10;
            var x = ReadUInt16LittleEndian(data, offset);
            var y = ReadUInt16LittleEndian(data, offset + 2);
            var z = ReadUInt16LittleEndian(data, offset + 4);
            var radius = ReadUInt16LittleEndian(data, offset + 6);
            lights[index] = new(index, new(x / 64f, y / 64f, z / 64f), radius / 64f,
                x, y, z, radius,
                ReadUInt16LittleEndian(data, offset + 8),
                ReadUInt16LittleEndian(data, offset + 0xa),
                ReadUInt16LittleEndian(data, offset + 0xc),
                ReadUInt16LittleEndian(data, offset + 0xe));
        }
        var xMasks = data.Slice(0x10, PointMaskSize).ToArray();
        var yMasks = data.Slice(0x10 + PointMaskSize, PointMaskSize).ToArray();
        return new(lights, xMasks, yMasks, MasksMatch(lights, xMasks, yMasks));
    }

    public static UyaEnvironmentSamplePoint[] ReadEnvironmentSamplePoints(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return [];
        var count = ReadCount(data, "environment sample point");
        EnsureRange(data, 0x10, checked(count * 0x20), "UYA environment sample point records");
        var points = new UyaEnvironmentSamplePoint[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 0x10 + index * 0x20;
            var x = ReadInt16LittleEndian(data, offset + 4);
            var y = ReadInt16LittleEndian(data, offset + 6);
            var z = ReadInt16LittleEndian(data, offset + 8);
            points[index] = new(index, new(x / 4f, y / 4f, z / 4f),
                ReadInt32LittleEndian(data, offset), x, y, z,
                ReadInt16LittleEndian(data, offset + 0xa), ReadInt16LittleEndian(data, offset + 0xc),
                data[offset + 0xe], data[offset + 0xf], ReadRgb24(data, offset + 0x10),
                data[offset + 0x13], data[offset + 0x14], data[offset + 0x15], data[offset + 0x16],
                ReadRgb24(data, offset + 0x17), ReadInt16LittleEndian(data, offset + 0x1a),
                ReadInt16LittleEndian(data, offset + 0x1c), ReadUInt16LittleEndian(data, offset + 0x1e));
        }
        return points;
    }

    public static UyaEnvironmentTransition[] ReadEnvironmentTransitions(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return [];
        var count = ReadCount(data, "environment transition");
        EnsureRange(data, 0x10, checked(count * 0x10), "UYA environment transition bounds");
        var recordsOffset = checked(0x10 + count * 0x10);
        EnsureRange(data, recordsOffset, checked(count * 0x80), "UYA environment transition records");
        var transitions = new UyaEnvironmentTransition[count];
        for (var index = 0; index < count; index++)
        {
            var offset = recordsOffset + index * 0x80;
            transitions[index] = new(index, ReadVector4(data, 0x10 + index * 0x10), ReadFloats(data, offset, 16),
                ReadRgba32(data, offset + 0x40), ReadRgba32(data, offset + 0x44),
                ReadInt32LittleEndian(data, offset + 0x48), ReadInt32LittleEndian(data, offset + 0x4c),
                ReadUInt32LittleEndian(data, offset + 0x50), ReadRgba32(data, offset + 0x54),
                ReadRgba32(data, offset + 0x58), ReadSingleLittleEndian(data, offset + 0x5c),
                ReadSingleLittleEndian(data, offset + 0x60), ReadSingleLittleEndian(data, offset + 0x64),
                ReadSingleLittleEndian(data, offset + 0x68), ReadSingleLittleEndian(data, offset + 0x6c),
                ReadSingleLittleEndian(data, offset + 0x70), ReadSingleLittleEndian(data, offset + 0x74),
                ReadSingleLittleEndian(data, offset + 0x78), ReadInt32LittleEndian(data, offset + 0x7c));
        }
        return transitions;
    }

    public static byte[][] ReadTieAmbientRgbas(ReadOnlySpan<byte> data, int tieCount)
    {
        if (tieCount < 0) throw new ArgumentOutOfRangeException(nameof(tieCount));
        var result = Enumerable.Range(0, tieCount).Select(_ => Array.Empty<byte>()).ToArray();
        if (data.IsEmpty) return result;
        var seen = new HashSet<int>();
        var offset = 0;
        while (true)
        {
            EnsureRange(data, offset, 2, "UYA tie ambient index");
            var index = ReadInt16LittleEndian(data, offset);
            offset += 2;
            if (index == -1) return result;
            if (index < 0 || index >= tieCount || !seen.Add(index))
                throw new InvalidDataException($"UYA tie ambient entry has invalid or duplicate tie index {index}.");
            EnsureRange(data, offset, 2, $"UYA tie ambient {index} size");
            var wordCount = ReadInt16LittleEndian(data, offset);
            offset += 2;
            if (wordCount < 0) throw new InvalidDataException($"UYA tie ambient {index} size cannot be negative.");
            var byteCount = checked(wordCount * 2);
            result[index] = SliceToArray(data, offset, byteCount, $"UYA tie ambient {index} words");
            offset = checked(offset + byteCount);
        }
    }

    private static int ReadCount(ReadOnlySpan<byte> data, string family)
    {
        EnsureRange(data, 0, 0x10, $"UYA {family} header");
        var count = ReadInt32LittleEndian(data, 0);
        if (count < 0) throw new InvalidDataException($"UYA {family} count cannot be negative.");
        return count;
    }

    private static bool MasksMatch(IReadOnlyList<UyaPointLight> lights, byte[] xMasks, byte[] yMasks)
    {
        var expectedX = new byte[PointMaskSize];
        var expectedY = new byte[PointMaskSize];
        foreach (var light in lights)
        {
            AddMask(expectedX, light.Index, light.Position.X, light.Radius);
            AddMask(expectedY, light.Index, light.Position.Y, light.Radius);
        }
        return expectedX.SequenceEqual(xMasks) && expectedY.SequenceEqual(yMasks);
    }

    internal static void AddMask(Span<byte> masks, int lightIndex, float position, float radius)
    {
        for (var cell = 0; cell < 64; cell++)
            if (position - radius < (cell + 1) * 16f && position + radius > cell * 16f)
                masks[cell * 16 + (lightIndex >> 3)] |= (byte)(1 << (lightIndex & 7));
    }

    private static GameplayVector4 ReadVector4(ReadOnlySpan<byte> data, int offset) => new(
        ReadSingleLittleEndian(data, offset), ReadSingleLittleEndian(data, offset + 4),
        ReadSingleLittleEndian(data, offset + 8), ReadSingleLittleEndian(data, offset + 12));

    private static float[] ReadFloats(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new float[count];
        for (var index = 0; index < count; index++) values[index] = ReadSingleLittleEndian(data, offset + index * 4);
        return values;
    }

    private static UyaRgb24 ReadRgb24(ReadOnlySpan<byte> data, int offset) =>
        new(data[offset], data[offset + 1], data[offset + 2]);

    private static UyaRgba32 ReadRgba32(ReadOnlySpan<byte> data, int offset) =>
        new(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]);

    private static byte[] FindPayload(IReadOnlyList<GameplayRawBlock> blocks, string name) =>
        blocks.FirstOrDefault(block => block.SemanticName == name)?.PayloadBytes ?? [];
}

public static class UyaPointLightsWriter
{
    public static byte[] Write(ReadOnlySpan<byte> source, IReadOnlyList<UyaPointLightEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0) return source.ToArray();
        var table = UyaGameplayLightingReader.ReadPointLights(source);
        if (!table.MasksMatchDerived)
            throw new InvalidDataException("UYA point-light masks do not match the source lights.");
        var output = source.ToArray();
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (edit.SourceIndex < 0 || edit.SourceIndex >= table.Lights.Length || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException("UYA point-light edit index is invalid or duplicated.");
            var offset = checked(0x10 + UyaGameplayLightingReader.PointMaskSize * 2 + edit.SourceIndex * 0x10);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset), Quantize(edit.Position.X, "X position"));
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset + 2), Quantize(edit.Position.Y, "Y position"));
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset + 4), Quantize(edit.Position.Z, "Z position"));
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset + 6), Quantize(edit.Radius, "radius"));
        }

        output.AsSpan(0x10, UyaGameplayLightingReader.PointMaskSize * 2).Clear();
        foreach (var light in UyaGameplayLightingReader.ReadPointLights(output).Lights)
        {
            UyaGameplayLightingReader.AddMask(
                output.AsSpan(0x10, UyaGameplayLightingReader.PointMaskSize),
                light.Index, light.Position.X, light.Radius);
            UyaGameplayLightingReader.AddMask(
                output.AsSpan(0x10 + UyaGameplayLightingReader.PointMaskSize, UyaGameplayLightingReader.PointMaskSize),
                light.Index, light.Position.Y, light.Radius);
        }
        return output;
    }

    private static ushort Quantize(float value, string field)
    {
        if (!float.IsFinite(value) || value < 0 || value > ushort.MaxValue / 64f)
            throw new InvalidDataException($"UYA point-light {field} is outside the unsigned 1/64-unit range.");
        return checked((ushort)MathF.Round(value * 64, MidpointRounding.AwayFromZero));
    }
}

public static class UyaDirectionalLightsWriter
{
    public static byte[] Write(ReadOnlySpan<byte> source, IReadOnlyList<UyaDirectionalLightEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0) return source.ToArray();
        var lights = UyaGameplayLightingReader.ReadDirectionalLights(source);
        var output = source.ToArray();
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (edit.SourceIndex < 0 || edit.SourceIndex >= lights.Length || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException("UYA directional-light edit index is invalid or duplicated.");
            UyaInstanceTransformWriter.Validate(new(0, 0, 0), edit.Rotation, new(1, 1, 1), "directional light");
            var rotation = Quaternion.Normalize(new(
                edit.Rotation.X, edit.Rotation.Y, edit.Rotation.Z, edit.Rotation.W));
            var light = lights[edit.SourceIndex];
            WriteDirection(output.AsSpan(0x10 + edit.SourceIndex * 0x40), 0x10, light.TopDirection, rotation);
            WriteDirection(output.AsSpan(0x10 + edit.SourceIndex * 0x40), 0x30, light.InverseDirection, rotation);
        }
        return output;
    }

    private static void WriteDirection(Span<byte> record, int offset, GameplayVector4 value, Quaternion rotation)
    {
        var direction = Vector3.Transform(new(value.X, value.Y, value.Z), rotation);
        UyaInstanceTransformWriter.WriteVector4(record, offset, direction.X, direction.Y, direction.Z, value.W);
    }
}

public static class UyaEnvironmentSamplePointsWriter
{
    public static byte[] Write(ReadOnlySpan<byte> source, IReadOnlyList<UyaEnvironmentSamplePointEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0) return source.ToArray();
        var points = UyaGameplayLightingReader.ReadEnvironmentSamplePoints(source);
        var output = source.ToArray();
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (edit.SourceIndex < 0 || edit.SourceIndex >= points.Length || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException("UYA environment-sample edit index is invalid or duplicated.");
            var offset = checked(0x10 + edit.SourceIndex * 0x20);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset + 4), Quantize(edit.Position.X));
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset + 6), Quantize(edit.Position.Y));
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset + 8), Quantize(edit.Position.Z));
        }
        return output;
    }

    private static short Quantize(float value)
    {
        if (!float.IsFinite(value) || value < short.MinValue / 4f || value > short.MaxValue / 4f)
            throw new InvalidDataException("UYA environment-sample position is outside the signed 1/4-unit range.");
        return checked((short)MathF.Round(value * 4, MidpointRounding.AwayFromZero));
    }
}

public static class UyaEnvironmentTransitionsWriter
{
    public static byte[] Write(ReadOnlySpan<byte> source, IReadOnlyList<UyaInstanceTransformEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0) return source.ToArray();
        var transitions = UyaGameplayLightingReader.ReadEnvironmentTransitions(source);
        var output = source.ToArray();
        var recordsOffset = checked(0x10 + transitions.Length * 0x10);
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (edit.SourceIndex < 0 || edit.SourceIndex >= transitions.Length || !seen.Add(edit.SourceIndex))
                throw new InvalidDataException("UYA environment-transition edit index is invalid or duplicated.");
            var matrix = UyaInstanceTransformWriter.CreateMatrix(edit, "environment transition");
            if (!System.Numerics.Matrix4x4.Invert(matrix, out var inverse))
                throw new InvalidDataException("UYA environment-transition transform is not invertible.");
            var radius = MathF.Sqrt(edit.Scale.X * edit.Scale.X
                + edit.Scale.Y * edit.Scale.Y + edit.Scale.Z * edit.Scale.Z);
            UyaInstanceTransformWriter.WriteVector4(output.AsSpan(0x10 + edit.SourceIndex * 0x10), 0,
                edit.Position.X, edit.Position.Y, edit.Position.Z, radius);
            var record = output.AsSpan(recordsOffset + edit.SourceIndex * 0x80, 0x80);
            UyaInstanceTransformWriter.WriteVector4(record, 0, inverse.M11, inverse.M12, inverse.M13, inverse.M14);
            UyaInstanceTransformWriter.WriteVector4(record, 0x10, inverse.M21, inverse.M22, inverse.M23, inverse.M24);
            UyaInstanceTransformWriter.WriteVector4(record, 0x20, inverse.M31, inverse.M32, inverse.M33, inverse.M34);
            UyaInstanceTransformWriter.WriteVector4(record, 0x30, inverse.M41, inverse.M42, inverse.M43, inverse.M44);
        }
        return output;
    }
}

public static class UyaTieAmbientRgbasWriter
{
    public static byte[] Write(IReadOnlyList<byte[]> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > short.MaxValue)
            throw new InvalidDataException("UYA tie ambient table exceeds the signed 16-bit index limit.");
        var size = sizeof(short);
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length % 2 != 0 || value.Length / 2 > short.MaxValue)
                throw new InvalidDataException("UYA tie ambient data must contain a signed 16-bit count of words.");
            if (value.Length > 0) size = checked(size + sizeof(short) * 2 + value.Length);
        }

        var output = new byte[size];
        var offset = 0;
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value.Length == 0) continue;
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset), (short)index);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset + 2), (short)(value.Length / 2));
            value.CopyTo(output, offset + 4);
            offset += sizeof(short) * 2 + value.Length;
        }
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset), -1);
        return output;
    }
}
