using System.Numerics;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.GC.Skyboxes;

public static class GcSkyRotationReader
{
    private const uint GlobalPointer = 0x001AEFF0;
    private const int ShellCount = 10;
    private const int RotationFunctionWordCount = 28;

    public static IReadOnlyDictionary<int, Vector3> ReadRadiansPerFrame(byte[] overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var segments = ReadSegments(overlay);
        var velocityPointerAddress = FindVelocityPointerAddress(overlay, segments);
        if (velocityPointerAddress is null)
        {
            return new Dictionary<int, Vector3>();
        }

        foreach (var segment in segments.Where(segment => segment.Flags == 1))
        {
            var tableAddress = FindTableAddress(overlay, segment, velocityPointerAddress.Value);
            if (tableAddress is not null && TryReadTable(overlay, segments, tableAddress.Value, out var rotations))
            {
                return rotations;
            }
        }

        return new Dictionary<int, Vector3>();
    }

    private static IReadOnlyList<OverlaySegment> ReadSegments(byte[] bytes)
    {
        var segments = new List<OverlaySegment>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 0x10)
            {
                return [];
            }

            var address = ReadUInt32LittleEndian(bytes, offset);
            var length = ReadUInt32LittleEndian(bytes, offset + 4);
            var flags = ReadUInt32LittleEndian(bytes, offset + 8);
            if (length > int.MaxValue || (long)offset + 0x10 + length > bytes.Length)
            {
                return [];
            }

            segments.Add(new OverlaySegment(address, offset + 0x10, (int)length, flags));
            offset = checked(offset + 0x10 + (int)length);
        }

        return segments;
    }

    private static uint? FindVelocityPointerAddress(byte[] bytes, IReadOnlyList<OverlaySegment> segments)
    {
        foreach (var segment in segments.Where(segment => segment.Flags == 1))
        {
            for (var offset = segment.FileOffset; offset + (RotationFunctionWordCount * 4) <= segment.EndOffset; offset += 4)
            {
                if (ReadUInt32LittleEndian(bytes, offset) != 0x27BDFFC0
                    || ReadUInt32LittleEndian(bytes, offset + 4) != 0x2403000C
                    || ReadUInt32LittleEndian(bytes, offset + 8) != 0xFFB10018
                    || ReadUInt32LittleEndian(bytes, offset + 12) != 0x00838818)
                {
                    continue;
                }

                var first = ReadAbsoluteLoadAddress(bytes, offset + 20);
                var second = ReadAbsoluteLoadAddress(bytes, offset + 68);
                var third = ReadAbsoluteLoadAddress(bytes, offset + 104);
                if (first is not null && first == second && first == third)
                {
                    return first;
                }
            }
        }

        return null;
    }

    private static uint? FindTableAddress(byte[] bytes, OverlaySegment segment, uint pointerAddress)
    {
        var gpOffset = (long)pointerAddress - GlobalPointer;
        if (gpOffset is < short.MinValue or > short.MaxValue)
        {
            return null;
        }

        var storeWord = 0xAF820000u | (ushort)(short)gpOffset;
        for (var offset = segment.FileOffset; offset + 4 <= segment.EndOffset; offset += 4)
        {
            if (ReadUInt32LittleEndian(bytes, offset) != storeWord)
            {
                continue;
            }

            for (var addOffset = offset - 4; addOffset >= Math.Max(segment.FileOffset, offset - 48); addOffset -= 4)
            {
                var addWord = ReadUInt32LittleEndian(bytes, addOffset);
                if ((addWord & 0xFFFF0000u) != 0x24420000u)
                {
                    continue;
                }

                for (var luiOffset = addOffset - 4; luiOffset >= Math.Max(segment.FileOffset, addOffset - 48); luiOffset -= 4)
                {
                    var luiWord = ReadUInt32LittleEndian(bytes, luiOffset);
                    if ((luiWord & 0xFFFF0000u) == 0x3C020000u)
                    {
                        return ((luiWord & 0xFFFFu) << 16) + unchecked((uint)(short)addWord);
                    }
                }
            }
        }

        return null;
    }

    private static bool TryReadTable(
        byte[] bytes,
        IReadOnlyList<OverlaySegment> segments,
        uint tableAddress,
        out IReadOnlyDictionary<int, Vector3> rotations)
    {
        rotations = new Dictionary<int, Vector3>();
        var tableLength = ShellCount * 3 * sizeof(float);
        var segment = segments.FirstOrDefault(segment =>
            segment.Flags == 1
            && tableAddress >= segment.Address
            && (ulong)tableAddress + (uint)tableLength <= (ulong)segment.Address + (uint)segment.Length);
        if (segment.Length == 0)
        {
            return false;
        }

        var tableOffset = checked(segment.FileOffset + (int)(tableAddress - segment.Address));
        var result = new Dictionary<int, Vector3>();
        for (var shell = 0; shell < ShellCount; shell++)
        {
            var offset = tableOffset + (shell * 3 * sizeof(float));
            var velocity = new Vector3(ReadSingleLittleEndian(bytes, offset), ReadSingleLittleEndian(bytes, offset + 4), ReadSingleLittleEndian(bytes, offset + 8));
            if (!float.IsFinite(velocity.X)
                || !float.IsFinite(velocity.Y)
                || !float.IsFinite(velocity.Z)
                || MathF.Abs(velocity.X) > 0.1f
                || MathF.Abs(velocity.Y) > 0.1f
                || MathF.Abs(velocity.Z) > 0.1f)
            {
                return false;
            }

            if (velocity != Vector3.Zero)
            {
                result[shell] = velocity;
            }
        }

        rotations = result;
        return true;
    }

    private static uint? ReadAbsoluteLoadAddress(byte[] bytes, int offset)
    {
        var luiWord = ReadUInt32LittleEndian(bytes, offset);
        var loadWord = ReadUInt32LittleEndian(bytes, offset + 4);
        if ((luiWord & 0xFFFF0000u) != 0x3C020000u || (loadWord & 0xFFFF0000u) != 0x8C420000u)
        {
            return null;
        }

        return ((luiWord & 0xFFFFu) << 16) + unchecked((uint)(short)loadWord);
    }

    private readonly record struct OverlaySegment(uint Address, int FileOffset, int Length, uint Flags)
    {
        public int EndOffset => FileOffset + Length;
    }
}
