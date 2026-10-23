using System.Buffers.Binary;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Sounds;
using RatchetPs2.Sdk;

internal static class SoundBankReaderTests
{
    public static void Run()
    {
        var bank = CreateDirectBank();
        var inventory = SoundBankReader.Inspect(GameId.UYA, bank);
        Expect(inventory is { Format: "989snd-sblk", FormatVersion: 3, BankId: 0x00574144 },
            "sound-bank identity");
        Expect(inventory.Programs.Count == 3 && inventory.Samples.Count == 2,
            "sound-bank inventory counts");
        Expect(inventory.Programs.All(value => value.PreviewSupported && value.ContentId?.Length == 64),
            "direct programs should have canonical preview identities");
        Expect(inventory.Programs[0].ContentId == inventory.Programs[1].ContentId,
            "identical direct programs should share canonical identity");
        Expect(inventory.Programs[0].ContentId
            == "942eace604c4780f3bcaa2b7f69b4b4c27cebf7824d83a2d31abd9e3deec223b",
            "direct program canonical identity");
        Expect(inventory.Programs[0].SampleIds.Single() == inventory.Programs[1].SampleIds.Single(),
            "direct programs should share sample identity");
        Expect(inventory.Samples[0].ContentId
            == "40d11228716b897fba478ef28b5548e9e160d94bb83dc42a9ee5ddf0c59f7abc",
            "shared sample canonical identity");
        Expect(inventory.Samples[0] is
            { EncodedOffset: 0, EncodedLength: 32, DecodedFrameCount: 56,
              IsLooping: true, LoopStartFrame: 0, LoopEndFrame: 56 },
            "looped sample inventory");
        var dlInventory = SoundBankReader.Inspect(GameId.DL, bank);
        Expect(dlInventory.Programs.Select(value => value.ContentId)
                .SequenceEqual(inventory.Programs.Select(value => value.ContentId)),
            "shared 989snd inventory is game-neutral");
        var dlPreview = SoundBankReader.RenderPreview(GameId.DL, bank, 0);
        Expect(dlPreview.Pcm16.SequenceEqual(
                SoundBankReader.RenderPreview(GameId.UYA, bank, 0).Pcm16),
            "shared 989snd preview is game-neutral");
        Expect(SoundBankReader.PreviewSampleRate(GameId.UYA, -116, 56) == 1_938
            && SoundBankReader.PreviewSampleRate(GameId.DL, -116, 56) == 1_938,
            "shared 989snd preview rate preserves center-fine direction");

        VerifyComposition(bank, inventory);

        var preview = SoundBankReader.RenderPreview(GameId.UYA, bank, 0);
        Expect(preview.SampleRate == 44_100 && preview.Channels == 1 && preview.Pcm16.Length == 56,
            "direct preview rate and frame count");
        Expect(preview.Pcm16[0] == 4096 && preview.Pcm16[1] == 8192
            && preview.Pcm16[27] == 8192 && preview.Pcm16[28] == 12288
            && preview.Pcm16[^1] == 16384,
            "PS2 ADPCM golden samples");
        Expect(preview is { IsLooping: true, LoopStartFrame: 0, LoopEndFrame: 56, PanDegrees: 0 }
            && preview.Gain == 1f && !preview.IsVariable, "direct preview playback metadata");
        var fineBank = bank.ToArray();
        var metadataOffset = ReadInt32(fineBank, 8);
        var fineGrainDataOffset = ReadInt32(fineBank, metadataOffset + 0x34);
        fineBank[metadataOffset + fineGrainDataOffset + 3] = 64;
        Expect(SoundBankReader.RenderPreview(GameId.UYA, fineBank, 0).SampleRate == 45_392,
            "sceSdNote2Pitch center-fine direction");
        var ps2RatePreview = SoundBankReader.RenderPreview(GameId.UYA, bank, 2);
        Expect(ps2RatePreview.SampleRate == 48_000
            && ps2RatePreview.Pcm16[0] == -4096
            && ps2RatePreview.Pcm16[1] == short.MinValue,
            "PS2-rate direct preview");
        ExpectThrows<InvalidDataException>(() =>
            SoundBankReader.RenderPreview(GameId.UYA, bank, 0, maximumFrames: 55));

        var duplicateSamples = bank.ToArray();
        var duplicateSampleDataOffset = ReadInt32(duplicateSamples, 16);
        duplicateSamples[duplicateSampleDataOffset + 1] = 1;
        duplicateSamples.AsSpan(duplicateSampleDataOffset, 16)
            .CopyTo(duplicateSamples.AsSpan(duplicateSampleDataOffset + 32, 16));
        var duplicateInventory = SoundBankReader.Inspect(GameId.UYA, duplicateSamples);
        Expect(duplicateInventory.Samples.Select(value => value.ContentId).Distinct().Count() == 1,
            "duplicate encoded samples should share canonical identity");
        Expect(SoundBankReader.RenderPreview(GameId.UYA, duplicateSamples, 2).Pcm16.Length == 28,
            "direct preview should resolve duplicate-content samples by native offset");
        duplicateSamples[metadataOffset + 0x40 + 4] = 2;
        Expect(SoundBankReader.RenderPreview(
                GameId.UYA, duplicateSamples, 0, maximumFrames: 64).Pcm16.Length > 0,
            "compound preview should resolve duplicate-content samples by native offset");

        var tone2 = bank.ToArray();
        var grainsOffset = ReadInt32(tone2, metadataOffset + 0x20);
        WriteUInt32(tone2, metadataOffset + grainsOffset + 2 * 8, 0x09000030);
        var tone2Program = SoundBankReader.Inspect(GameId.UYA, tone2).Programs[2];
        Expect(tone2Program.PreviewSupported && tone2Program.ContentId?.Length == 64,
            "tone2 operation should use the recovered tone handler");
        var tone2Preview = SoundBankReader.RenderPreview(GameId.UYA, tone2, 2, maximumFrames: 64);
        Expect(tone2Preview.Channels == 2 && tone2Preview.Pcm16.Length > 0,
            "tone2 operation should render through the compound preview mixer");

        var compound = bank.ToArray();
        compound[metadataOffset + 0x40 + 4] = 2;
        var compoundProgram = SoundBankReader.Inspect(GameId.UYA, compound).Programs[0];
        Expect(compoundProgram.PreviewSupported && compoundProgram.Operations.Count == 2,
            "multi-grain program should be previewable");
        var compoundPreview = SoundBankReader.RenderPreview(GameId.UYA, compound, 0, maximumFrames: 1_000);
        Expect(compoundPreview is { Channels: 2, SampleRate: 48_000 }
            && compoundPreview.Pcm16.Length == 2_000,
            "multi-grain preview should produce bounded stereo PCM");

        var variable = compound.ToArray();
        WriteUInt32(variable, metadataOffset + grainsOffset, 0x1b000064);
        var variablePreview = SoundBankReader.RenderPreview(GameId.UYA, variable, 0, maximumFrames: 1_000);
        Expect(variablePreview.IsVariable, "random-pitch programs should bypass reusable preview caches");

        var auditionGate = CreateAuditionGateBank();
        var audiblePreview = SoundBankReader.RenderPreview(
            GameId.UYA, auditionGate, 0, maximumFrames: 1_000);
        Expect(audiblePreview.IsVariable && audiblePreview.Pcm16.Length > 2
            && audiblePreview.Pcm16.Any(value => value != 0),
            "audition preview should advance past sparse variable no-voice branches");
        var authenticPreview = SoundBankReader.RenderPreview(
            GameId.UYA, auditionGate, 0, maximumFrames: 1_000, preferAudible: false);
        Expect(authenticPreview.Pcm16.Length == 2 && authenticPreview.Pcm16.All(value => value == 0),
            "authentic preview should retain the variable no-voice branch");

        var selection = bank.ToArray();
        selection[metadataOffset + 0x40 + 4] = 3;
        WriteUInt32(selection, metadataOffset + grainsOffset, 0x19000102);
        var firstSelection = SoundBankReader.RenderPreview(GameId.UYA, selection, 0, maximumFrames: 1_000);
        var secondSelection = SoundBankReader.RenderPreview(GameId.UYA, selection, 0, maximumFrames: 1_000);
        Expect(firstSelection.IsVariable && firstSelection.Pcm16[0] < 0 && secondSelection.Pcm16[0] > 0,
            "RAND_PLAY should retain its previous branch between previews");

        var lfo = compound.ToArray();
        WriteUInt32(lfo, metadataOffset + grainsOffset, 0x04000000);
        var lfoParameters = lfo.AsSpan(metadataOffset + fineGrainDataOffset, 16);
        lfoParameters.Clear();
        lfoParameters[1] = 3;
        lfoParameters[3] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(lfoParameters[6..], 128);
        var lfoSampleDataOffset = ReadInt32(lfo, 0x10);
        lfo[lfoSampleDataOffset + 1] = 0;
        lfo[lfoSampleDataOffset + 17] = 1;
        var lfoPreview = SoundBankReader.RenderPreview(GameId.UYA, lfo, 0, maximumFrames: 1_000);
        Expect(lfoPreview.Pcm16.Length / lfoPreview.Channels < 55,
            "989snd sine LFO should start at its positive peak");

        var badPredictor = bank.ToArray();
        badPredictor[ReadInt32(badPredictor, 16)] = 0x50;
        ExpectThrows<InvalidDataException>(() => SoundBankReader.Inspect(GameId.UYA, badPredictor));
        var badParameter = bank.ToArray();
        WriteUInt32(badParameter, metadataOffset + grainsOffset, 0x01ffffff);
        ExpectThrows<InvalidDataException>(() => SoundBankReader.Inspect(GameId.UYA, badParameter));
        var overlapping = bank.ToArray();
        var grainDataOffset = ReadInt32(overlapping, metadataOffset + 0x34);
        WriteUInt32(overlapping, metadataOffset + grainDataOffset + 2 * 24 + 0x10, 16);
        ExpectThrows<InvalidDataException>(() => SoundBankReader.Inspect(GameId.UYA, overlapping));
        ExpectThrows<InvalidDataException>(() =>
            SoundBankReader.Inspect(GameId.UYA, CreateReferenceCycleBank()));

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            ExpectThrows<OperationCanceledException>(() =>
                SoundBankReader.Inspect(GameId.UYA, bank, cancelled.Token));
        }

        VerifyRemapInventory();

        InspectCorpusWhenAvailable();
        InspectDlCorpusWhenAvailable();
    }

    private static void VerifyComposition(byte[] bank, SoundBankInventory inventory)
    {
        var samples = inventory.Samples.Select(value => new SoundBankSampleInput(
            $"sample:{value.EncodedOffset}",
            bank.AsSpan(value.Provenance.Offset, value.Provenance.Length).ToArray())).ToArray();
        var sampleKeys = inventory.Samples.ToDictionary(
            value => value.EncodedOffset, value => $"sample:{value.EncodedOffset}");
        var programs = inventory.Programs.Select(value => new SoundBankProgramInput(
            $"program:{value.NativeIndex}", value.Volume, value.VolumeGroup, value.Pan,
            value.InstanceLimit, value.Flags,
            value.Operations.Select(operation => new SoundBankOperationInput(
                operation.Type, operation.Argument0, operation.Argument1, operation.Argument2,
                operation.Delay, operation.ParameterBytes,
                operation.Tone is not { } tone ? null : new(
                    tone.Priority, tone.Volume, tone.CenterNote, tone.CenterFine, tone.Pan,
                    tone.NoteLow, tone.NoteHigh, tone.PitchBendLow, tone.PitchBendHigh,
                    tone.Adsr1, tone.Adsr2, tone.Flags, tone.Reserved,
                    sampleKeys[tone.SampleOffset]),
                operation.ReferencedProgramIndex is { } target ? $"program:{target}" : null)).ToArray()))
            .ToArray();
        var rebuilt = SoundBankComposer.Compose(GameId.UYA, bank, programs, samples);
        var rebuiltInventory = SoundBankReader.Inspect(GameId.UYA, rebuilt.BankBytes);
        Expect(rebuiltInventory.Programs.Select(value => value.ContentId)
                .SequenceEqual(inventory.Programs.Select(value => value.ContentId))
            && rebuiltInventory.Samples.Select(value => value.ContentId)
                .SequenceEqual(inventory.Samples.Select(value => value.ContentId)),
            "989snd composition should semantically preserve rebuilt programs and samples");
        var sourceMetadata = bank.AsSpan(
            inventory.MetadataProvenance.Offset, inventory.MetadataProvenance.Length);
        var rebuiltMetadata = rebuilt.BankBytes.AsSpan(
            rebuiltInventory.MetadataProvenance.Offset, rebuiltInventory.MetadataProvenance.Length);
        var sourceGrainData = BinaryPrimitives.ReadInt32LittleEndian(sourceMetadata[0x34..]);
        var rebuiltGrainData = BinaryPrimitives.ReadInt32LittleEndian(rebuiltMetadata[0x34..]);
        Expect(rebuiltMetadata[rebuiltGrainData..].StartsWith(sourceMetadata[sourceGrainData..])
            && rebuilt.BankBytes.AsSpan(
                    rebuiltInventory.SampleDataProvenance.Offset,
                    inventory.SampleDataProvenance.Length)
                .SequenceEqual(bank.AsSpan(
                    inventory.SampleDataProvenance.Offset,
                    inventory.SampleDataProvenance.Length)),
            "989snd composition should retain source grain-data and sample-chunk opaque bytes");

        var childParameters = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(childParameters.AsSpan(0x0c), 99);
        var appended = new SoundBankProgramInput(
            "program:appended", 127, 0, 0, 1, 0,
            [new(5, 0, 0, 0, 0, childParameters, null, "program:0")]);
        var extended = SoundBankComposer.Compose(GameId.UYA, bank, [.. programs, appended], samples);
        var repeated = SoundBankComposer.Compose(GameId.UYA, bank, [.. programs, appended], samples);
        var extendedInventory = SoundBankReader.Inspect(GameId.UYA, extended.BankBytes);
        Expect(extendedInventory.Programs.Count == inventory.Programs.Count + 1
            && extendedInventory.Samples.Count == inventory.Samples.Count
            && extendedInventory.Programs[^1].Operations.Single().ReferencedProgramIndex == 0,
            "989snd composition should append programs, reuse samples, and rewrite child slots");
        var reordered = SoundBankComposer.Compose(
            GameId.UYA, bank, [programs[1], programs[0], programs[2], appended], samples);
        Expect(SoundBankReader.Inspect(GameId.UYA, reordered.BankBytes).Programs[^1].ContentId
                == extendedInventory.Programs[^1].ContentId,
            "compound sound identity should use child asset identity rather than its final native slot");
        Expect(extended.BankBytes.Length % 2048 == 0,
            "989snd composition should retain sector alignment");
        Expect(extended.BankBytes.SequenceEqual(repeated.BankBytes),
            "989snd composition should be deterministic");
        ExpectThrows<InvalidDataException>(() => SoundBankComposer.Compose(
            GameId.UYA, bank,
            [.. programs, appended with
            {
                Operations = [appended.Operations[0] with { ReferencedProgramKey = "missing" }],
            }], samples));
        ExpectThrows<InvalidDataException>(() => SoundBankComposer.Compose(
            GameId.UYA, bank, [programs[0], programs[0]], samples));
        ExpectThrows<InvalidDataException>(() => SoundBankComposer.Compose(
            GameId.UYA, bank, programs,
            [.. samples, new("sample:over-budget", new byte[2 * 1024 * 1024])]));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        ExpectThrows<OperationCanceledException>(() => SoundBankComposer.Compose(
            GameId.UYA, bank, programs, samples, cancelled.Token));
    }

    private static void VerifyRemapInventory()
    {
        var header = CreateAssetHeader();
        var inventory = SoundBankReader.InspectRemap(GameId.UYA, header, 2);
        Expect(inventory.Mobys.Count == 2
            && inventory.Mobys[0].ClassId == 0x222
            && inventory.Mobys[1].ClassId == 0x111,
            "sound-remap inventory preserves moby definition order");
        Expect(inventory.Mobys[0].Slots.Select(value => value.NativeProgramIndex)
                .SequenceEqual([1, -1])
            && inventory.Mobys[1].Slots.Single().NativeProgramIndex == 0
            && inventory.Globals.Single().NativeProgramIndex == 1,
            "sound-remap inventory resolves ordered native program slots");
        Expect(inventory is
            { Provenance: { Offset: 0x100, Length: 64 }, OtherRecordCount: 1,
              OtherRecordsProvenance: { Offset: 0x110, Length: 0x20 } },
            "sound-remap inventory retains exact source and opaque ranges");

        var composition = new SoundRemapComposition(
            3,
            inventory.Mobys.ToDictionary(
                value => value.ClassId,
                value => (IReadOnlyList<int>)value.Slots.Select(slot =>
                    slot.NativeProgramIndex == 1 ? 2 : slot.NativeProgramIndex).ToArray()),
            inventory.Globals.Select(value => value.NativeProgramIndex).ToArray(),
            header.AsSpan(
                inventory.OtherRecordsProvenance.Offset,
                inventory.OtherRecordsProvenance.Length).ToArray());
        var composedHeader = SoundBankComposer.ComposeRemap(GameId.UYA, header, composition);
        var composed = SoundBankReader.InspectRemap(GameId.UYA, composedHeader, 3);
        Expect(composed.Mobys[0].Slots.Select(value => value.NativeProgramIndex)
                .SequenceEqual([2, -1])
            && composed.Globals.Single().NativeProgramIndex == 1,
            "sound-remap composition should rewrite final slots and retain global slots");
        Expect(composedHeader.AsSpan(
                composed.OtherRecordsProvenance.Offset,
                composed.OtherRecordsProvenance.Length).SequenceEqual(
                    header.AsSpan(
                        inventory.OtherRecordsProvenance.Offset,
                        inventory.OtherRecordsProvenance.Length)),
            "sound-remap composition should preserve opaque other records");

        var invalid = header.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(0x100 + 52), 2);
        ExpectThrows<InvalidDataException>(() =>
            SoundBankReader.InspectRemap(GameId.UYA, invalid, 2));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        ExpectThrows<OperationCanceledException>(() =>
            SoundBankReader.InspectRemap(GameId.UYA, header, 2, cancelled.Token));
    }

    private static byte[] CreateAssetHeader()
    {
        const int modelsOffset = 0xc0;
        const int remapOffset = 0x100;
        var bytes = new byte[0x180];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x18), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x1c), modelsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x70), remapOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(modelsOffset + 4), 0x222);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(modelsOffset + 0x20 + 4), 0x111);
        var remap = bytes.AsSpan(remapOffset);
        BinaryPrimitives.WriteInt16LittleEndian(remap, 16);
        BinaryPrimitives.WriteInt16LittleEndian(remap[2..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(remap[4..], 48);
        BinaryPrimitives.WriteInt16LittleEndian(remap[6..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(remap[8..], 52);
        BinaryPrimitives.WriteInt16LittleEndian(remap[10..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(remap[12..], 60);
        BinaryPrimitives.WriteInt16LittleEndian(remap[14..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(remap[48..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(remap[52..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(remap[56..], -1);
        BinaryPrimitives.WriteInt32LittleEndian(remap[60..], 0);
        return bytes;
    }

    private static void InspectCorpusWhenAvailable()
    {
        var root = Path.Combine("test-assets", "extractions_uya");
        if (!Directory.Exists(root)) return;
        var checkedBanks = 0;
        var opcodeCases = new Dictionary<byte, (byte[] Bank, int Program)>();
        foreach (var directory in Directory.EnumerateDirectories(root, "level*_iso_world01"))
        {
            var path = Path.Combine(directory, "level_wad", "sound.bnk");
            if (!File.Exists(path)) continue;
            var bytes = File.ReadAllBytes(path);
            var first = SoundBankReader.Inspect(GameId.UYA, bytes);
            var second = SoundBankReader.Inspect(GameId.UYA, bytes);
            Expect(first.Programs.Count > 0 && first.Samples.Count > 0
                && first.Programs.All(value => value.PreviewSupported),
                $"{Path.GetFileName(directory)} production inventory");
            Expect(first.Programs.Select(value => value.ContentId)
                .SequenceEqual(second.Programs.Select(value => value.ContentId)),
                $"{Path.GetFileName(directory)} deterministic identities");
            foreach (var program in first.Programs)
                foreach (var operation in program.Operations)
                    opcodeCases.TryAdd(operation.Type, (bytes, program.NativeIndex));
            checkedBanks++;
        }
        if (checkedBanks > 0)
        {
            foreach (var pair in opcodeCases)
            {
                var preview = SoundBankReader.RenderPreview(
                    GameId.UYA, pair.Value.Bank, pair.Value.Program, maximumFrames: 300_000);
                Expect(preview.Pcm16.Length > 0 && preview.Channels is 1 or 2,
                    $"production opcode 0x{pair.Key:x2} preview");
            }
            Console.WriteLine($"UYA 989snd reader: inventoried {checkedBanks} bank(s).");
        }
    }

    private static void InspectDlCorpusWhenAvailable()
    {
        var root = Path.Combine("test-assets", "extractions");
        if (!Directory.Exists(root)) return;
        var checkedBanks = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.bnk", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(path);
            var first = SoundBankReader.Inspect(GameId.DL, bytes);
            var second = SoundBankReader.Inspect(GameId.DL, bytes);
            Expect(first.Programs.Count > 0 && first.Samples.Count > 0,
                $"{Path.GetRelativePath(root, path)} Deadlocked inventory");
            Expect(first.Programs.Select(value => value.ContentId)
                    .SequenceEqual(second.Programs.Select(value => value.ContentId)),
                $"{Path.GetRelativePath(root, path)} deterministic identities");
            checkedBanks++;
        }
        if (checkedBanks > 0)
            Console.WriteLine($"Deadlocked 989snd reader: inventoried {checkedBanks} bank(s).");
    }

    private static byte[] CreateDirectBank()
    {
        const int metadataOffset = 0x18;
        const int programCount = 3;
        const int grainCount = 3;
        const int sampleCount = 2;
        const int programsOffset = 0x40;
        const int grainsOffset = programsOffset + programCount * 12;
        const int grainDataOffset = grainsOffset + grainCount * 8;
        const int metadataLength = grainDataOffset + grainCount * 24;
        const int sampleDataOffset = metadataOffset + metadataLength;
        const int sampleDataLength = 48;
        var bytes = new byte[2048];
        WriteContainer(bytes, metadataOffset, metadataLength, sampleDataOffset, sampleDataLength);
        WriteBlockHeader(
            bytes.AsSpan(metadataOffset, metadataLength),
            programCount, grainCount, sampleCount,
            programsOffset, grainsOffset, grainDataOffset, sampleDataLength);
        var metadata = bytes.AsSpan(metadataOffset, metadataLength);
        for (var index = 0; index < programCount; index++)
        {
            var program = metadata[(programsOffset + index * 12)..];
            program[0] = 127;
            program[1] = 1;
            program[4] = 1;
            program[5] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(program[8..], checked((uint)(index * 8)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                metadata[(grainsOffset + index * 8)..],
                0x01000000u | checked((uint)(index * 24)));
            WriteTone(metadata[(grainDataOffset + index * 24)..],
                index == 2 ? (sbyte)-60 : (sbyte)60,
                index == 2 ? 32 : 0);
        }
        WriteFrame(bytes.AsSpan(sampleDataOffset), flags: 4, packedSamples: 0x21);
        WriteFrame(bytes.AsSpan(sampleDataOffset + 16), flags: 3, packedSamples: 0x43);
        WriteFrame(bytes.AsSpan(sampleDataOffset + 32), flags: 1, packedSamples: 0x8f);
        return bytes;
    }

    private static byte[] CreateReferenceCycleBank()
    {
        const int metadataOffset = 0x18;
        const int programCount = 2;
        const int grainCount = 2;
        const int programsOffset = 0x40;
        const int grainsOffset = programsOffset + programCount * 12;
        const int grainDataOffset = grainsOffset + grainCount * 8;
        const int metadataLength = grainDataOffset + grainCount * 32;
        const int sampleDataOffset = metadataOffset + metadataLength;
        var bytes = new byte[2048];
        WriteContainer(bytes, metadataOffset, metadataLength, sampleDataOffset, 0);
        WriteBlockHeader(
            bytes.AsSpan(metadataOffset, metadataLength),
            programCount, grainCount, 0,
            programsOffset, grainsOffset, grainDataOffset, 0);
        var metadata = bytes.AsSpan(metadataOffset, metadataLength);
        for (var index = 0; index < programCount; index++)
        {
            var program = metadata[(programsOffset + index * 12)..];
            program[0] = 127;
            program[4] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(program[8..], checked((uint)(index * 8)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                metadata[(grainsOffset + index * 8)..],
                0x05000000u | checked((uint)(index * 32)));
            BinaryPrimitives.WriteInt32LittleEndian(
                metadata[(grainDataOffset + index * 32 + 0x0c)..], 1 - index);
        }
        return bytes;
    }

    private static byte[] CreateAuditionGateBank()
    {
        const int metadataOffset = 0x18;
        const int selectionCount = 40;
        const int grainCount = selectionCount + 1;
        const int programsOffset = 0x40;
        const int grainsOffset = programsOffset + 12;
        const int grainDataOffset = grainsOffset + grainCount * 8;
        const int metadataLength = grainDataOffset + 24;
        const int sampleDataOffset = metadataOffset + metadataLength;
        const int sampleDataLength = 32;
        var bytes = new byte[2048];
        WriteContainer(bytes, metadataOffset, metadataLength, sampleDataOffset, sampleDataLength);
        WriteBlockHeader(
            bytes.AsSpan(metadataOffset, metadataLength),
            programCount: 1, grainCount, sampleCount: 1,
            programsOffset, grainsOffset, grainDataOffset, sampleDataLength);
        var metadata = bytes.AsSpan(metadataOffset, metadataLength);
        var program = metadata[programsOffset..];
        program[0] = 127;
        program[1] = 1;
        program[4] = grainCount;
        program[5] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(metadata[grainsOffset..], 0x27000128);
        BinaryPrimitives.WriteUInt32LittleEndian(
            metadata[(grainsOffset + selectionCount * 8)..], 0x01000000);
        WriteTone(metadata[grainDataOffset..], centerNote: 60, sampleOffset: 0);
        WriteFrame(bytes.AsSpan(sampleDataOffset), flags: 0, packedSamples: 0x21);
        WriteFrame(bytes.AsSpan(sampleDataOffset + 16), flags: 1, packedSamples: 0x43);
        return bytes;
    }

    private static void WriteContainer(
        Span<byte> bytes,
        int metadataOffset,
        int metadataLength,
        int sampleDataOffset,
        int sampleDataLength)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], metadataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0x0c..], metadataLength);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0x10..], sampleDataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0x14..], sampleDataLength);
    }

    private static void WriteBlockHeader(
        Span<byte> block,
        short programCount,
        short grainCount,
        short sampleCount,
        int programsOffset,
        int grainsOffset,
        int grainDataOffset,
        int sampleDataLength)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(block, 0x6b6c4253);
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(block[8..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(block[0x0c..], 0x00574144);
        BinaryPrimitives.WriteInt16LittleEndian(block[0x16..], programCount);
        BinaryPrimitives.WriteInt16LittleEndian(block[0x18..], grainCount);
        BinaryPrimitives.WriteInt16LittleEndian(block[0x1a..], sampleCount);
        BinaryPrimitives.WriteInt32LittleEndian(block[0x1c..], programsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(block[0x20..], grainsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(block[0x28..], sampleDataLength);
        BinaryPrimitives.WriteInt32LittleEndian(block[0x2c..], sampleDataLength);
        BinaryPrimitives.WriteInt32LittleEndian(block[0x34..], grainDataOffset);
    }

    private static void WriteTone(Span<byte> tone, sbyte centerNote, int sampleOffset)
    {
        tone[0] = 100;
        tone[1] = 127;
        tone[2] = unchecked((byte)centerNote);
        tone[6] = 0;
        tone[7] = 127;
        BinaryPrimitives.WriteUInt32LittleEndian(tone[0x10..], checked((uint)sampleOffset));
    }

    private static void WriteFrame(Span<byte> frame, byte flags, byte packedSamples)
    {
        frame[0] = 0;
        frame[1] = flags;
        frame.Slice(2, 14).Fill(packedSamples);
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);

    private static void WriteUInt32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);

    private static void Expect(bool condition, string context)
    {
        if (!condition) throw new InvalidOperationException(context);
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
