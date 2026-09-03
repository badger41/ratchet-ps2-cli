namespace RatchetPs2.Core.Ties;

internal static class TieClassHeaderReader
{
    public static TieClassHeader Read(BinaryReader reader, bool useRc1Header)
    {
        ArgumentNullException.ThrowIfNull(reader);

        reader.BaseStream.Position = 0;

        return useRc1Header ? ReadRc1(reader) : ReadLaterGame(reader);
    }

    private static TieClassHeader ReadRc1(BinaryReader reader)
    {
        var packetTableOffsets = ReadUInt32s(reader, 3);
        var vertexNormalsOffset = reader.ReadUInt32();
        var nearDistance = reader.ReadSingle();
        var mediumDistance = reader.ReadSingle();
        var farDistance = reader.ReadSingle();
        _ = reader.ReadUInt32();
        var packetCounts = reader.ReadBytes(3);
        var textureCount = reader.ReadByte();
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        var shadersOffset = reader.ReadUInt32();
        var vertexNormalsCount = vertexNormalsOffset > 0
            && shadersOffset >= vertexNormalsOffset
            && (shadersOffset - vertexNormalsOffset) % 8 == 0
            && (shadersOffset - vertexNormalsOffset) / 8 <= short.MaxValue
                ? (short)((shadersOffset - vertexNormalsOffset) / 8)
                : (short)0;
        var boundingSphere = new TieBoundingSphere(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());
        var scale = reader.ReadSingle();

        return new TieClassHeader
        {
            PacketTableOffsets = packetTableOffsets,
            PacketCounts = packetCounts,
            TextureCount = textureCount,
            NearDistance = nearDistance,
            MediumDistance = mediumDistance,
            FarDistance = farDistance,
            ShadersOffset = shadersOffset,
            VertexNormalsOffset = vertexNormalsOffset,
            VertexNormalsCount = vertexNormalsCount,
            CacheSizes = new short[3],
            RgbaRemapOffsets = new ushort[3],
            Scale = scale,
            BoundingSphere = boundingSphere,
            Lods = new TieLod[3],
            UnknownOffsets78 = new ushort[3]
        };
    }

    private static TieClassHeader ReadLaterGame(BinaryReader reader)
    {

        var packetTableOffsets = ReadUInt32s(reader, 3);

        var packetCounts = new byte[3];
        for (var i = 0; i < packetCounts.Length; i++)
        {
            packetCounts[i] = reader.ReadByte();
        }

        var textureCount = reader.ReadByte();
        var nearDistance = reader.ReadSingle();
        var mediumDistance = reader.ReadSingle();
        var farDistance = reader.ReadSingle();
        var shadersOffset = reader.ReadUInt32();
        var instanceIndex = reader.ReadInt32();

        var cacheSizes = new short[3];
        for (var i = 0; i < cacheSizes.Length; i++)
        {
            cacheSizes[i] = reader.ReadInt16();
        }

        var rgbaRemapOffsets = new ushort[3];
        for (var i = 0; i < rgbaRemapOffsets.Length; i++)
        {
            rgbaRemapOffsets[i] = reader.ReadUInt16();
        }

        var ambientRgbaOffset = reader.ReadUInt32();
        var vertexNormalsOffset = reader.ReadUInt32();
        var vertexNormalsCount = reader.ReadInt16();
        var ambientSize = reader.ReadInt16();
        var modeBits = reader.ReadInt16();
        var instanceCount = reader.ReadInt16();
        var scale = reader.ReadSingle();
        var oClass = reader.ReadInt16();
        var tClass = reader.ReadInt16();
        var mipmapDistance = reader.ReadSingle();
        var glowRgba = reader.ReadInt32();
        var boundingSphere = new TieBoundingSphere(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());

        var lods = new TieLod[3];
        for (var i = 0; i < lods.Length; i++)
        {
            lods[i] = new TieLod(
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16());
        }

        var unknownOffsets78 = new ushort[3];
        for (var i = 0; i < unknownOffsets78.Length; i++)
        {
            unknownOffsets78[i] = reader.ReadUInt16();
        }

        return new TieClassHeader
        {
            PacketTableOffsets = packetTableOffsets,
            PacketCounts = packetCounts,
            TextureCount = textureCount,
            NearDistance = nearDistance,
            MediumDistance = mediumDistance,
            FarDistance = farDistance,
            ShadersOffset = shadersOffset,
            InstanceIndex = instanceIndex,
            CacheSizes = cacheSizes,
            RgbaRemapOffsets = rgbaRemapOffsets,
            AmbientRgbaOffset = ambientRgbaOffset,
            VertexNormalsOffset = vertexNormalsOffset,
            VertexNormalsCount = vertexNormalsCount,
            AmbientSize = ambientSize,
            ModeBits = modeBits,
            InstanceCount = instanceCount,
            Scale = scale,
            OClass = oClass,
            TClass = tClass,
            MipmapDistance = mipmapDistance,
            GlowRgba = glowRgba,
            BoundingSphere = boundingSphere,
            Lods = lods,
            UnknownOffsets78 = unknownOffsets78,
            Padding = reader.ReadInt16()
        };
    }

    private static uint[] ReadUInt32s(BinaryReader reader, int count)
    {
        var values = new uint[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadUInt32();
        }

        return values;
    }
}
