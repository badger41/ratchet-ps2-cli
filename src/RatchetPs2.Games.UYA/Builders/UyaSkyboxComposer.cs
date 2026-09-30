using System.Numerics;
using System.Runtime.InteropServices;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Skyboxes;

namespace RatchetPs2.Games.UYA.Builders;

internal static class UyaSkyboxComposer
{
    private const int HeaderSize = 0x20;
    private const int TextureDefinitionSize = 0x10;
    private const int TextureDefinitionTrailerSize = 0x20;
    private const int SpriteSize = 0x20;
    private const int ShellHeaderSize = 0x10;
    private const int ClusterHeaderSize = 0x20;
    private const float RuntimeFrameRate = 60f;

    internal sealed record ShellComposition(
        ReadOnlyMemory<byte> SkyboxBytes,
        int ShellIndex,
        Vector3? InitialRotationRadians,
        Vector3? AngularVelocityRadiansPerSecond);

    internal sealed record CompositionResult(byte[] Bytes, bool IsBasePassThrough);

    private sealed record ComposedShell(
        short Flags,
        short RotationX,
        short RotationY,
        short RotationZ,
        short RotationDeltaX,
        short RotationDeltaY,
        short RotationDeltaZ,
        IReadOnlyList<ComposedCluster> Clusters);

    private sealed record ComposedCluster(SkyboxCluster Source, byte[] Data);

    internal static CompositionResult Compose(
        ReadOnlySpan<byte> baseSkyboxBytes,
        IReadOnlyList<ShellComposition> shellCompositions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (shellCompositions.Count > SkyboxFormat.MaxShellCount)
        {
            throw new InvalidDataException(
                $"UYA skyboxes support at most {SkyboxFormat.MaxShellCount} shells; received {shellCompositions.Count}.");
        }

        var baseBytes = baseSkyboxBytes.ToArray();
        var baseSkybox = ReadSkybox(baseBytes, "Target base skybox");
        if (baseSkybox.Header.SpriteMax < baseSkybox.Header.SpriteCount)
        {
            throw new InvalidDataException(
                $"Target base skybox sprite capacity {baseSkybox.Header.SpriteMax} is below its sprite count {baseSkybox.Header.SpriteCount}.");
        }

        var textures = baseSkybox.Textures.ToList();
        EnsureTextureCount(textures.Count);
        var shells = new List<ComposedShell>(shellCompositions.Count);
        var isBasePassThrough = shellCompositions.Count == baseSkybox.Shells.Count;
        var parsedSources = new Dictionary<ReadOnlyMemory<byte>, (Skybox Skybox, bool UsesBaseBytes)>();
        var preparedShells = new Dictionary<(ReadOnlyMemory<byte> Bytes, int ShellIndex),
            (SkyboxShell Source, IReadOnlyList<ComposedCluster> Clusters)>();

        for (var compositionIndex = 0; compositionIndex < shellCompositions.Count; compositionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var composition = shellCompositions[compositionIndex];
            if (!parsedSources.TryGetValue(composition.SkyboxBytes, out var parsed))
            {
                var matchesBase = composition.SkyboxBytes.Span.SequenceEqual(baseBytes);
                parsed = (matchesBase
                    ? baseSkybox
                    : ReadSkybox(composition.SkyboxBytes, $"Source skybox for composed shell {compositionIndex}"),
                    matchesBase);
                parsedSources.Add(composition.SkyboxBytes, parsed);
            }
            var (sourceSkybox, usesBaseBytes) = parsed;
            if ((uint)composition.ShellIndex >= (uint)sourceSkybox.Shells.Count)
            {
                throw new InvalidDataException(
                    $"Source skybox shell index {composition.ShellIndex} is outside its 0..{sourceSkybox.Shells.Count - 1} range.");
            }

            if (!preparedShells.TryGetValue((composition.SkyboxBytes, composition.ShellIndex), out var prepared))
            {
                var source = sourceSkybox.Shells[composition.ShellIndex];
                prepared = (source, PrepareClusters(sourceSkybox, source, composition.ShellIndex, textures, cancellationToken));
                preparedShells.Add((composition.SkyboxBytes, composition.ShellIndex), prepared);
            }
            var sourceShell = prepared.Source;
            var rotation = composition.InitialRotationRadians is { } rotationRadians
                ? Quantize(rotationRadians, SkyboxFormat.RotationTickRadians, "initial rotation")
                : (sourceShell.RotationX, sourceShell.RotationY, sourceShell.RotationZ);
            var rotationDelta = composition.AngularVelocityRadiansPerSecond is { } angularVelocity
                ? Quantize(angularVelocity, SkyboxFormat.RotationTickRadians * RuntimeFrameRate, "angular velocity")
                : (sourceShell.RotationDeltaX, sourceShell.RotationDeltaY, sourceShell.RotationDeltaZ);

            isBasePassThrough &= usesBaseBytes
                && composition.ShellIndex == compositionIndex
                && rotation == (sourceShell.RotationX, sourceShell.RotationY, sourceShell.RotationZ)
                && rotationDelta == (sourceShell.RotationDeltaX, sourceShell.RotationDeltaY, sourceShell.RotationDeltaZ);

            shells.Add(new ComposedShell(
                sourceShell.Flags,
                rotation.Item1,
                rotation.Item2,
                rotation.Item3,
                rotationDelta.Item1,
                rotationDelta.Item2,
                rotationDelta.Item3,
                prepared.Clusters));
        }

        if (isBasePassThrough)
        {
            return new CompositionResult(baseBytes, true);
        }

        var output = Write(baseBytes, baseSkybox, textures, shells, cancellationToken);
        Verify(output, baseSkybox, textures, shells);
        return new CompositionResult(output, false);
    }

    private static IReadOnlyList<ComposedCluster> PrepareClusters(
        Skybox sourceSkybox,
        SkyboxShell sourceShell,
        int sourceShellIndex,
        List<SkyboxTexture> textures,
        CancellationToken cancellationToken)
    {
        var textureRemap = new Dictionary<byte, byte>();
        var clusters = new List<ComposedCluster>(sourceShell.Clusters.Count);
        foreach (var cluster in sourceShell.Clusters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = cluster.Data.ToArray();
            for (var triangleIndex = 0; triangleIndex < cluster.Triangles.Count; triangleIndex++)
            {
                var sourceTextureId = cluster.Triangles[triangleIndex].TextureId;
                if (sourceTextureId == byte.MaxValue) continue;
                if (sourceTextureId >= sourceSkybox.Textures.Count)
                {
                    throw new InvalidDataException(
                        $"Source shell {sourceShellIndex} cluster {cluster.Index} triangle {triangleIndex} " +
                        $"references missing texture {sourceTextureId}.");
                }
                if (!textureRemap.TryGetValue(sourceTextureId, out var targetTextureId))
                {
                    var sourceTexture = sourceSkybox.Textures[sourceTextureId];
                    var existingIndex = textures.FindIndex(texture => TexturesEqual(texture, sourceTexture));
                    if (existingIndex < 0)
                    {
                        EnsureTextureCount(textures.Count + 1);
                        existingIndex = textures.Count;
                        textures.Add(sourceTexture);
                    }
                    targetTextureId = checked((byte)existingIndex);
                    textureRemap.Add(sourceTextureId, targetTextureId);
                }
                data[checked(cluster.TriangleOffset + (triangleIndex * 4) + 3)] = targetTextureId;
            }
            clusters.Add(new ComposedCluster(cluster, data));
        }
        return clusters;
    }

    private static Skybox ReadSkybox(ReadOnlyMemory<byte> bytes, string label)
    {
        try
        {
            using var stream = MemoryMarshal.TryGetArray(bytes, out var segment)
                ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
                : new MemoryStream(bytes.ToArray(), writable: false);
            return SkyboxReader.Read(stream, GameId.UYA);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or OverflowException)
        {
            throw new InvalidDataException($"{label} is not a valid UYA skybox: {exception.Message}", exception);
        }
    }

    private static byte[] Write(
        byte[] baseBytes,
        Skybox baseSkybox,
        IReadOnlyList<SkyboxTexture> textures,
        IReadOnlyList<ComposedShell> shells,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteZeros(writer, HeaderSize + (SkyboxFormat.MaxShellCount * sizeof(uint)));

        var fxListOffset = CheckedUInt(stream.Position);
        if (baseSkybox.FxList is { } fxList)
        {
            writer.Write(fxList);
        }

        Align(writer, 0x10);
        var textureDefinitionOffset = CheckedUInt(stream.Position);
        WriteZeros(writer, checked((textures.Count * TextureDefinitionSize) + TextureDefinitionTrailerSize));
        var textureDataOffset = CheckedUInt(stream.Position);
        var textureOffsets = new List<(uint Palette, uint Pixels)>(textures.Count);
        foreach (var texture in textures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paletteOffset = CheckedUInt(stream.Position - textureDataOffset);
            writer.Write(texture.PaletteData);
            var pixelOffset = CheckedUInt(stream.Position - textureDataOffset);
            writer.Write(texture.PixelData);
            textureOffsets.Add((paletteOffset, pixelOffset));
        }

        uint spritesOffset = 0;
        if (baseSkybox.Header.SpriteMax > 0)
        {
            spritesOffset = CheckedUInt(stream.Position);
            var sourceOffset = checked((int)baseSkybox.Header.SpritesOffset);
            var sourceLength = checked(baseSkybox.Header.SpriteMax * SpriteSize);
            if (sourceOffset < 0 || sourceOffset + sourceLength > baseBytes.Length)
            {
                throw new InvalidDataException("Target base skybox sprite allocation is outside the input.");
            }

            writer.Write(baseBytes, sourceOffset, sourceLength);
        }

        Align(writer, 0x10);
        var shellOffsets = new List<uint>(shells.Count);
        foreach (var shell in shells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shellOffset = CheckedUInt(stream.Position);
            shellOffsets.Add(shellOffset);
            WriteZeros(writer, checked(ShellHeaderSize + (shell.Clusters.Count * ClusterHeaderSize)));
            var clusterDataOffsets = new List<uint>(shell.Clusters.Count);
            foreach (var cluster in shell.Clusters)
            {
                Align(writer, 0x10);
                clusterDataOffsets.Add(CheckedUInt(stream.Position));
                writer.Write(cluster.Data);
            }

            var endOffset = stream.Position;
            stream.Position = shellOffset;
            writer.Write(checked((short)shell.Clusters.Count));
            writer.Write(shell.Flags);
            writer.Write(shell.RotationX);
            writer.Write(shell.RotationY);
            writer.Write(shell.RotationZ);
            writer.Write(shell.RotationDeltaX);
            writer.Write(shell.RotationDeltaY);
            writer.Write(shell.RotationDeltaZ);
            for (var clusterIndex = 0; clusterIndex < shell.Clusters.Count; clusterIndex++)
            {
                var cluster = shell.Clusters[clusterIndex].Source;
                writer.Write(cluster.Sphere.X);
                writer.Write(cluster.Sphere.Y);
                writer.Write(cluster.Sphere.Z);
                writer.Write(cluster.Sphere.Radius);
                writer.Write(clusterDataOffsets[clusterIndex]);
                writer.Write(cluster.VertexCount);
                writer.Write(cluster.TriangleCount);
                writer.Write(cluster.VertexOffset);
                writer.Write(cluster.TexCoordOffset);
                writer.Write(cluster.TriangleOffset);
                writer.Write(checked((short)shell.Clusters[clusterIndex].Data.Length));
            }

            stream.Position = endOffset;
        }

        var finalLength = stream.Position;
        stream.Position = 0;
        writer.Write(baseSkybox.Header.Color.R);
        writer.Write(baseSkybox.Header.Color.G);
        writer.Write(baseSkybox.Header.Color.B);
        writer.Write(baseSkybox.Header.Color.A);
        writer.Write(baseSkybox.Header.ClearScreen);
        writer.Write(checked((short)shells.Count));
        writer.Write(baseSkybox.Header.SpriteCount);
        writer.Write(baseSkybox.Header.SpriteMax);
        writer.Write(checked((short)textures.Count));
        writer.Write(baseSkybox.Header.FxCount);
        writer.Write(textureDefinitionOffset);
        writer.Write(textureDataOffset);
        writer.Write(checked((int)fxListOffset));
        writer.Write(spritesOffset);
        foreach (var shellOffset in shellOffsets)
        {
            writer.Write(shellOffset);
        }

        stream.Position = textureDefinitionOffset;
        for (var textureIndex = 0; textureIndex < textures.Count; textureIndex++)
        {
            writer.Write(textureOffsets[textureIndex].Palette);
            writer.Write(textureOffsets[textureIndex].Pixels);
            writer.Write(textures[textureIndex].Width);
            writer.Write(textures[textureIndex].Height);
        }

        stream.Position = finalLength;
        writer.Flush();
        return stream.ToArray();
    }

    private static void Verify(
        byte[] output,
        Skybox baseSkybox,
        IReadOnlyList<SkyboxTexture> textures,
        IReadOnlyList<ComposedShell> shells)
    {
        var composed = ReadSkybox(output, "Composed skybox");
        Ensure(composed.Header.Color == baseSkybox.Header.Color
            && composed.Header.ClearScreen == baseSkybox.Header.ClearScreen
            && composed.Header.SpriteCount == baseSkybox.Header.SpriteCount
            && composed.Header.SpriteMax == baseSkybox.Header.SpriteMax
            && composed.Header.FxCount == baseSkybox.Header.FxCount,
            "global header fields changed");
        Ensure((composed.FxList ?? []).SequenceEqual(baseSkybox.FxList ?? []), "FX bytes changed");
        Ensure(composed.Sprites.SequenceEqual(baseSkybox.Sprites), "sprite data changed");
        Ensure(composed.Textures.Count == textures.Count, "texture count changed");
        for (var textureIndex = 0; textureIndex < textures.Count; textureIndex++)
        {
            Ensure(TexturesEqual(composed.Textures[textureIndex], textures[textureIndex]),
                $"texture {textureIndex} changed");
        }

        Ensure(composed.Shells.Count == shells.Count, "shell count changed");
        for (var shellIndex = 0; shellIndex < shells.Count; shellIndex++)
        {
            var actualShell = composed.Shells[shellIndex];
            var expectedShell = shells[shellIndex];
            Ensure(actualShell.Flags == expectedShell.Flags
                && actualShell.RotationX == expectedShell.RotationX
                && actualShell.RotationY == expectedShell.RotationY
                && actualShell.RotationZ == expectedShell.RotationZ
                && actualShell.RotationDeltaX == expectedShell.RotationDeltaX
                && actualShell.RotationDeltaY == expectedShell.RotationDeltaY
                && actualShell.RotationDeltaZ == expectedShell.RotationDeltaZ,
                $"shell {shellIndex} flags or rotation changed");
            Ensure(actualShell.Clusters.Count == expectedShell.Clusters.Count,
                $"shell {shellIndex} cluster count changed");
            for (var clusterIndex = 0; clusterIndex < expectedShell.Clusters.Count; clusterIndex++)
            {
                var actualCluster = actualShell.Clusters[clusterIndex];
                var expectedCluster = expectedShell.Clusters[clusterIndex];
                Ensure(SpheresEqual(actualCluster.Sphere, expectedCluster.Source.Sphere)
                    && actualCluster.VertexCount == expectedCluster.Source.VertexCount
                    && actualCluster.TriangleCount == expectedCluster.Source.TriangleCount
                    && actualCluster.VertexOffset == expectedCluster.Source.VertexOffset
                    && actualCluster.TexCoordOffset == expectedCluster.Source.TexCoordOffset
                    && actualCluster.TriangleOffset == expectedCluster.Source.TriangleOffset
                    && actualCluster.Data.SequenceEqual(expectedCluster.Data),
                    $"shell {shellIndex} cluster {clusterIndex} changed");
                Ensure(actualCluster.Triangles.All(triangle =>
                        triangle.TextureId == byte.MaxValue || triangle.TextureId < textures.Count),
                    $"shell {shellIndex} cluster {clusterIndex} has an invalid texture reference");
            }
        }
    }

    private static (short X, short Y, short Z) Quantize(Vector3 value, float unit, string field)
    {
        return (
            Quantize(value.X, unit, $"{field} X"),
            Quantize(value.Y, unit, $"{field} Y"),
            Quantize(value.Z, unit, $"{field} Z"));
    }

    private static short Quantize(float value, float unit, string field)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException($"Skybox {field} must be finite.");
        }

        var ticks = MathF.Round(value / unit, MidpointRounding.AwayFromZero);
        if (ticks < short.MinValue || ticks > short.MaxValue)
        {
            throw new InvalidDataException(
                $"Skybox {field} {value} is outside the native signed 16-bit range.");
        }

        return (short)ticks;
    }

    private static bool TexturesEqual(SkyboxTexture left, SkyboxTexture right)
    {
        return left.Width == right.Width
            && left.Height == right.Height
            && left.PaletteData.SequenceEqual(right.PaletteData)
            && left.PixelData.SequenceEqual(right.PixelData);
    }

    private static bool SpheresEqual(SkyboxSphere left, SkyboxSphere right)
    {
        return BitConverter.SingleToInt32Bits(left.X) == BitConverter.SingleToInt32Bits(right.X)
            && BitConverter.SingleToInt32Bits(left.Y) == BitConverter.SingleToInt32Bits(right.Y)
            && BitConverter.SingleToInt32Bits(left.Z) == BitConverter.SingleToInt32Bits(right.Z)
            && BitConverter.SingleToInt32Bits(left.Radius) == BitConverter.SingleToInt32Bits(right.Radius);
    }

    private static void EnsureTextureCount(int count)
    {
        if (count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"Composed skybox texture count {count} exceeds the 255 usable texture IDs.");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException($"Composed UYA skybox validation failed: {message}.");
        }
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);

    private static void Align(BinaryWriter writer, int alignment)
    {
        var aligned = Align(checked((int)writer.BaseStream.Position), alignment);
        WriteZeros(writer, aligned - checked((int)writer.BaseStream.Position));
    }

    private static uint CheckedUInt(long value) => checked((uint)value);

    private static void WriteZeros(BinaryWriter writer, int count)
    {
        if (count > 0)
        {
            writer.Write(new byte[count]);
        }
    }
}
