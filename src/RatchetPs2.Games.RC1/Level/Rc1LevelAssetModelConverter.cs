using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.LevelAssets;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Skyboxes;

namespace RatchetPs2.Games.RC1.Level;

internal static class Rc1LevelAssetModelConverter
{
    public static byte[] Convert(FrontendAssetKind kind, byte[] bytes, int classId, CancellationToken token, IReadOnlyList<FrontendAssetTexture>? textures = null)
    {
        token.ThrowIfCancellationRequested();
        switch (kind)
        {
            case FrontendAssetKind.Tie: return Rc1TieModelConverter.Convert(bytes, classId, token, textures);
            case FrontendAssetKind.Moby:
                using (var input = new MemoryStream(bytes, writable: false))
                {
                    var model = MobyModelReader.Read(input, new() { ModelFormat = MobyModelFormat.Rc1 });
                    // Empty raw placeholders reserve null animation IDs without writing payloads.
                    var emptySlots = new List<int>();
                    for (var index = 0; index < model.AnimationCount; index++)
                        if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x48 + index * 4)) == 0)
                        {
                            emptySlots.Add(index);
                            model.Sequences.Insert(index, new() { RawData = [] });
                        }
                    model.ModelFormat = MobyModelFormat.Standard;
                    if (model.LowLodMeshCount > 0) model.Padding = model.JointCount;
                    var result = MobyModelPacker.Build(model);
                    foreach (var index in emptySlots) result.AsSpan(0x48 + index * 4, 4).Clear();
                    token.ThrowIfCancellationRequested();
                    _ = MobyModelReader.Read(new MemoryStream(result, writable: false));
                    return result;
                }
            case FrontendAssetKind.Shrub:
                using (var input = new MemoryStream(bytes, writable: false)) _ = ShrubClassReader.Read(input);
                var shrub = bytes.ToArray();
                BinaryPrimitives.WriteUInt16LittleEndian(shrub.AsSpan(0x24), checked((ushort)classId));
                return shrub;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public static byte[]? RepairPreviousTie(byte[] sourceBytes, byte[] convertedBytes, int previousClassId,
        int targetClassId, IReadOnlyList<FrontendAssetTexture>? textures, CancellationToken token)
    {
        var analysis = new Rc1TieModelConverter.Analysis(sourceBytes);
        var corrected = Rc1TieModelConverter.Convert(sourceBytes, targetClassId, token, textures, analysis);
        if (corrected.AsSpan().SequenceEqual(convertedBytes)) return corrected;
        var previous = Rc1TieModelConverter.Convert(sourceBytes, previousClassId, token, analysis: analysis);
        return previous.AsSpan().SequenceEqual(convertedBytes) ? corrected : null;
    }

    public static byte[] ConvertSky(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var input = new MemoryStream(bytes, writable: false);
        var source = SkyboxReader.Read(input, GameId.RC1);
        var result = bytes.ToArray();
        foreach (var shell in source.Shells)
        {
            token.ThrowIfCancellationRequested();
            var header = result.AsSpan(checked((int)shell.Offset), 16);
            header.Clear();
            BinaryPrimitives.WriteInt16LittleEndian(header, shell.ClusterCount);
            BinaryPrimitives.WriteInt16LittleEndian(header[2..], shell.Flags);
        }
        _ = SkyboxReader.Read(new MemoryStream(result, writable: false), GameId.UYA);
        return result;
    }
}
