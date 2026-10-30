using RatchetPs2.Games.UYA.Level;
using RatchetPs2.Core.Occlusion;
using System.Buffers.Binary;
using RatchetPs2.Core.Gameplay;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Occlusion;

public static class UyaOcclusionOceanPlanes
{
    public static float[] ReadHeights(IReadOnlyDictionary<string, byte[]> files, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!files.TryGetValue("gameplay/core/moby_instances.bin",out var bytes)) return [];
        var oceans=UyaMobyInstancesReader.Read(bytes).Instances.Select((m,i)=>(Moby:m,Index:i))
            .Where(p=>p.Moby.ClassId==0x0B37).ToArray();
        if (oceans.Length==0) return [];
        var names=new[]{"pvar_moby_links","pvar_table","pvar_data","pvar_relative_pointers"};
        var blocks=names.Select((name,i)=>new GameplayRawBlock(i,0,0,name,
            files.GetValueOrDefault($"gameplay/core/{name}.bin") ?? [])).ToArray();
        var tables=GameplayPvarTableReader.Read(blocks,"UYA");
        var heights=new List<float>();
        foreach (var (moby,index) in oceans)
        {
            token.ThrowIfCancellationRequested();
            if (tables is null || moby.PvarIndex<0 || moby.PvarIndex>=tables.Entries.Count
                || tables.Entries[moby.PvarIndex].Data.Length<80)
                throw new InvalidDataException($"Ocean Moby #{index} has no valid PVar surface height.");
            // The ocean uses zPosition at 0x4C, independently of the Moby transform.
            var height=BinaryPrimitives.ReadSingleLittleEndian(tables.Entries[moby.PvarIndex].Data.AsSpan(76));
            if (!float.IsFinite(height) || Math.Abs(height)>1000000)
                throw new InvalidDataException($"Ocean Moby #{index} has an invalid surface height.");
            heights.Add(height);
        }
        return heights.Distinct().Order().ToArray();
    }
}
