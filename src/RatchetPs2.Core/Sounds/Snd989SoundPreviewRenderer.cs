namespace RatchetPs2.Core.Sounds;

internal static partial class Snd989SoundPreviewRenderer
{
    public const int SampleRate = 48_000;
    private const int TickFrames = SampleRate / 240;
    private const int MaximumInstructions = 1_000_000;
    private const int MaximumAuditionAttempts = 4_096;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string ProgramId, int Grain), int>
        PreviewSelectionValues = new();

    public static short[] Render(
        byte[] bankBytes,
        SoundBankInventory inventory,
        int programIndex,
        int maximumFrames,
        bool preferAudible,
        CancellationToken cancellationToken)
    {
        short[] rendered = [];
        var samplesByOffset = inventory.Samples.ToDictionary(value => value.EncodedOffset);
        var attempts = preferAudible ? MaximumAuditionAttempts : 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var engine = new Engine(
                bankBytes, inventory, samplesByOffset, maximumFrames, cancellationToken);
            rendered = engine.Render(programIndex);
            if (engine.StartedVoice || !engine.EndedWithoutVoice) break;
        }
        return rendered;
    }

    private sealed partial class Engine
    {
        private readonly byte[] _bankBytes;
        private readonly SoundBankInventory _inventory;
        private readonly IReadOnlyDictionary<int, SoundSampleInfo> _samplesByOffset;
        private readonly int _maximumFrames;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, short[]> _decoded = new(StringComparer.Ordinal);
        private readonly List<Handler> _handlers = [];
        private readonly List<Voice> _voices = [];
        private readonly sbyte[] _globalRegisters = new sbyte[32];
        private readonly PreviewRandom _random;
        private int _instructionCount;
        private int _tick;

        public bool StartedVoice { get; private set; }
        public bool EndedWithoutVoice { get; private set; }

        public Engine(
            byte[] bankBytes,
            SoundBankInventory inventory,
            IReadOnlyDictionary<int, SoundSampleInfo> samplesByOffset,
            int maximumFrames,
            CancellationToken cancellationToken)
        {
            _bankBytes = bankBytes;
            _inventory = inventory;
            _samplesByOffset = samplesByOffset;
            _maximumFrames = maximumFrames;
            _cancellationToken = cancellationToken;
            _random = new((uint)Random.Shared.Next(1, int.MaxValue));
        }

        public short[] Render(int programIndex)
        {
            StartHandler(_inventory.Programs[programIndex], null, 1024, null, 0, 0);
            foreach (var handler in _handlers.ToArray()) RunReady(handler);
            if (_voices.Count == 0 && _handlers.All(value => value.Stopped || value.FlowEnded))
            {
                EndedWithoutVoice = true;
                return [0, 0];
            }
            var left = new float[_maximumFrames];
            var right = new float[_maximumFrames];
            var renderedFrames = 0;
            for (var frame = 0; frame < _maximumFrames; frame++)
            {
                if ((frame & 0x3fff) == 0) _cancellationToken.ThrowIfCancellationRequested();
                if (frame > 0 && frame % TickFrames == 0) Tick();
                foreach (var voice in _voices)
                {
                    if (!voice.Active) continue;
                    var sample = NextSample(voice);
                    if (!voice.Active && sample == 0) continue;
                    var volume = CurrentVolume(voice.Owner) / 127f * voice.Volume / 127f;
                    var pan = NormalizePan(CurrentPan(voice.Owner) + voice.Pan);
                    if ((voice.Tone.Flags & 0x10) == 0)
                    {
                        var position = MathF.Sin(pan * MathF.PI / 180f);
                        left[frame] += sample * volume * MathF.Sqrt((1f - position) / 2f);
                        right[frame] += sample * volume * MathF.Sqrt((1f + position) / 2f);
                    }
                }
                renderedFrames = frame + 1;
                _voices.RemoveAll(value => !value.Active);
                _handlers.RemoveAll(value => value.Stopped
                    || value.FlowEnded && !_voices.Any(voice => voice.Owner == value));
                if (_handlers.Count == 0 && _voices.Count == 0) break;
            }

            renderedFrames = Math.Max(1, renderedFrames);
            var pcm = new short[checked(renderedFrames * 2)];
            for (var frame = 0; frame < renderedFrames; frame++)
            {
                pcm[frame * 2] = ToPcm(left[frame]);
                pcm[frame * 2 + 1] = ToPcm(right[frame]);
            }
            return pcm;
        }

        private void Tick()
        {
            _tick++;
            if ((_tick & 1) == 0)
                foreach (var handler in _handlers.Where(value => !value.Stopped)) UpdateLfos(handler);
            foreach (var handler in _handlers.ToArray())
            {
                if (handler.Stopped || handler.FlowEnded) continue;
                handler.Countdown--;
                RunReady(handler);
            }
        }

        private Handler StartHandler(
            SoundProgramInfo program,
            Handler? parent,
            int appVolume,
            int? originalVolume,
            int pitchMod,
            int pitchBend)
        {
            var handler = new Handler(
                program, parent, appVolume, originalVolume ?? program.Volume, pitchMod, pitchBend);
            _handlers.Add(handler);
            return handler;
        }

        private void RunReady(Handler handler)
        {
            while (!handler.Stopped && !handler.FlowEnded && handler.Countdown <= 0)
            {
                if (++_instructionCount > MaximumInstructions)
                    throw new InvalidDataException("989snd preview exceeded its grain instruction budget.");
                if ((uint)handler.NextGrain >= (uint)handler.Program.Operations.Count)
                {
                    handler.FlowEnded = true;
                    return;
                }
                var operation = handler.Program.Operations[handler.NextGrain];
                var delay = Execute(handler, operation);
                if (delay < 0)
                {
                    StopHandler(handler);
                    return;
                }
                if (handler.Skipping)
                {
                    handler.GrainsToPlay--;
                    if (handler.GrainsToPlay == 0)
                    {
                        handler.NextGrain += handler.GrainsToSkip;
                        handler.Skipping = false;
                    }
                }
                handler.NextGrain++;
                if (handler.NextGrain >= handler.Program.Operations.Count)
                    handler.FlowEnded = true;
                else
                    handler.Countdown = checked(handler.Program.Operations[handler.NextGrain].Delay + delay);
            }
        }

        private int Execute(Handler handler, SoundOperationInfo operation)
        {
            switch (operation.Type)
            {
                case 0:
                case 2:
                case 3:
                case 0x14:
                case 0x15:
                case 0x23:
                    return 0;
                case 1:
                case 9:
                    StartVoice(handler, operation.Tone!);
                    return 0;
                case 4:
                    ConfigureLfo(handler, operation.ParameterBytes);
                    return 0;
                case 5:
                    StartChild(handler, operation);
                    return 0;
                case 6:
                    StopChildren(handler, operation.ReferencedProgramIndex);
                    return 0;
                case 8:
                    Branch(handler, operation);
                    return 0;
                case 0x16:
                    handler.NextGrain = FindPrevious(handler, 0x15) - 1;
                    return 0;
                case 0x17:
                    handler.NextGrain = FindNext(handler, 0x16);
                    return 0;
                case 0x18:
                    return -1;
                case 0x19:
                    BeginSelection(handler, operation, random: true);
                    return 0;
                case 0x1a:
                    return _random.Next(PackedArgument(operation) + 1);
                case 0x1b:
                    handler.AppPitchBend = (short)((_random.Next(0x7fff) * 0xffff / 0x7fff - 0x8000)
                        * operation.Argument0 / 100);
                    return 0;
                case 0x1c:
                    handler.AppPitchBend = operation.Argument0 < 0
                        ? (short)(-0x8000 * operation.Argument0 / -128)
                        : (short)(0x7fff * operation.Argument0 / 127);
                    return 0;
                case 0x1d:
                    handler.AppPitchBend = (short)Math.Clamp(
                        handler.AppPitchBend + 0x7fff * operation.Argument0 / 127,
                        short.MinValue, short.MaxValue);
                    return 0;
                case 0x1e:
                    WriteRegister(handler, operation.Argument0, operation.Argument1);
                    return 0;
                case 0x1f:
                    WriteRegister(handler, operation.Argument0, (sbyte)(_random.Next(
                        operation.Argument2 - operation.Argument1 + 1) + operation.Argument1));
                    return 0;
                case 0x20:
                    WriteRegister(handler, operation.Argument0,
                        (sbyte)Math.Min(127, ReadRegister(handler, operation.Argument0) + 1));
                    return 0;
                case 0x21:
                    WriteRegister(handler, operation.Argument0,
                        (sbyte)Math.Max(-128, ReadRegister(handler, operation.Argument0) - 1));
                    return 0;
                case 0x22:
                    var source = ReadRegister(handler, operation.Argument0);
                    var skip = operation.Argument1 switch
                    {
                        0 => source >= operation.Argument2,
                        1 => source != operation.Argument2,
                        2 => source <= operation.Argument2,
                        _ => throw new InvalidDataException("989snd preview has an invalid register test."),
                    };
                    if (skip) handler.NextGrain++;
                    return 0;
                case 0x24:
                    handler.NextGrain = FindMarker(handler, operation.Argument0) - 1;
                    return 0;
                case 0x25:
                    handler.NextGrain = FindMarker(handler, (sbyte)(_random.Next(
                        operation.Argument1 - operation.Argument0 + 1) + operation.Argument0)) - 1;
                    return 0;
                case 0x26:
                    if (_voices.Any(value => value.Active && value.Owner == handler))
                    {
                        handler.NextGrain--;
                        return 1;
                    }
                    return 0;
                case 0x27:
                    BeginSelection(handler, operation, random: false);
                    return 0;
                case 0x28:
                    WriteRegister(handler, operation.Argument1, (sbyte)Math.Clamp(
                        ReadRegister(handler, operation.Argument1) + operation.Argument0, -128, 127));
                    return 0;
                case 0x29:
                case 0x2a:
                    foreach (var voice in _voices.Where(value => value.Owner == handler)) voice.Active = false;
                    return 0;
                case 0x2b:
                    handler.NextGrain = handler.Program.Operations.Count - 1;
                    return 0;
                case 0x2c:
                    WriteRegister(handler, operation.Argument1, ReadRegister(handler, operation.Argument0));
                    return 0;
                default:
                    throw new NotSupportedException($"Operation type 0x{operation.Type:x2} is not supported for preview.");
            }
        }

        private void StartVoice(Handler handler, SoundToneInfo tone)
        {
            StartedVoice = true;
            var sample = _samplesByOffset[tone.SampleOffset];
            if (!_decoded.TryGetValue(sample.ContentId, out var pcm))
            {
                pcm = Ps2SpuAdpcmDecoder.Decode(
                    _bankBytes.AsSpan(sample.Provenance.Offset, sample.Provenance.Length),
                    sample.DecodedFrameCount,
                    _cancellationToken);
                _decoded.Add(sample.ContentId, pcm);
            }
            var volume = tone.Volume switch
            {
                >= 0 => tone.Volume,
                >= -4 => handler.Registers[-tone.Volume - 1],
                -5 => (sbyte)_random.Next(0x80),
                _ => _globalRegisters[-tone.Volume - 6],
            };
            var pan = tone.Pan switch
            {
                >= 0 => tone.Pan,
                >= -4 => 360 * handler.Registers[-tone.Pan - 1] / 127,
                -5 => _random.Next(360),
                _ => 360 * _globalRegisters[-tone.Pan - 6] / 127,
            };
            if ((handler.Program.Flags & 4) != 0) pan = CollapsePan(pan, handler.AppVolume);
            _voices.Add(new(handler, tone, sample, pcm, Math.Max(0, (int)volume), NormalizePan(pan)));
        }

        private float NextSample(Voice voice)
        {
            if ((voice.Tone.Flags & 8) != 0)
                return (_random.Next(0x10000) - 0x8000) / 32768f;
            var end = voice.Sample.LoopEndFrame;
            if (voice.Position >= end)
            {
                if (voice.Sample.IsLooping)
                {
                    var start = voice.Sample.LoopStartFrame ?? 0;
                    voice.Position = start + (voice.Position - start) % (end - start);
                }
                else
                {
                    voice.Active = false;
                    return 0;
                }
            }
            var index = Math.Min((int)voice.Position, voice.Pcm.Length - 1);
            var next = Math.Min(index + 1, voice.Pcm.Length - 1);
            var fraction = (float)(voice.Position - index);
            var sample = (voice.Pcm[index] + (voice.Pcm[next] - voice.Pcm[index]) * fraction) / 32768f;
            voice.Position += PitchRatio(voice.Owner, voice.Tone);
            return sample;
        }

        private static double PitchRatio(Handler handler, SoundToneInfo tone)
        {
            var bend = Math.Clamp(handler.AppPitchBend + handler.LfoPitchBend, short.MinValue, short.MaxValue);
            var bendRange = bend < 0 ? tone.PitchBendLow * bend / -32768d * -128d
                : tone.PitchBendHigh * bend / 32767d * 128d;
            var note = 60 * 128 + handler.AppPitchMod + handler.LfoPitchMod + bendRange;
            var center = Math.Abs((int)tone.CenterNote) * 128;
            var baseRate = tone.CenterNote < 0 ? 48_000d : 44_100d;
            return baseRate / SampleRate * Math.Pow(2d, (note - center + tone.CenterFine) / (12d * 128d));
        }

        private static int CurrentVolume(Handler handler) => Math.Clamp(
            (handler.AppVolume * handler.OriginalVolume >> 10) + handler.LfoVolume, 0, 127);

        private static int CurrentPan(Handler handler) => NormalizePan(handler.AppPan + handler.LfoPan);

        private static int CollapsePan(int pan, int volume)
        {
            const int minimum = 1024 / 6;
            const int full = 1024 - minimum;
            if (volume <= minimum) return 0;
            if (volume >= full) return pan;
            return pan > 180
                ? 360 - (360 - pan) * (volume - minimum) / (full - minimum)
                : pan * (volume - minimum) / (full - minimum);
        }

        private static int NormalizePan(int value)
        {
            value %= 360;
            return value < 0 ? value + 360 : value;
        }

        private static int PackedArgument(SoundOperationInfo value) =>
            (byte)value.Argument0 | (byte)value.Argument1 << 8 | (byte)value.Argument2 << 16;

        private static short ToPcm(float value) =>
            (short)Math.Clamp((int)Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
    }

    private sealed class Handler(
        SoundProgramInfo program,
        Handler? parent,
        int appVolume,
        int originalVolume,
        int appPitchMod,
        int appPitchBend)
    {
        public SoundProgramInfo Program = program;
        public Handler? Parent { get; } = parent;
        public int AppVolume { get; } = appVolume;
        public int OriginalVolume = originalVolume;
        public int AppPan { get; } = program.Pan;
        public sbyte[] Registers { get; } = new sbyte[4];
        public Lfo?[] Lfos { get; } = new Lfo?[4];
        public int NextGrain;
        public int Countdown = program.Operations.Count == 0 ? 0 : program.Operations[0].Delay;
        public int AppPitchMod { get; } = appPitchMod;
        public int AppPitchBend = appPitchBend;
        public int LfoPitchBend;
        public int LfoPitchMod;
        public int LfoVolume;
        public int LfoPan;
        public bool FlowEnded = program.Operations.Count == 0;
        public bool Stopped;
        public bool Skipping;
        public int GrainsToPlay;
        public int GrainsToSkip;
    }

    private sealed class Voice(
        Handler owner,
        SoundToneInfo tone,
        SoundSampleInfo sample,
        short[] pcm,
        int volume,
        int pan)
    {
        public Handler Owner { get; } = owner;
        public SoundToneInfo Tone { get; } = tone;
        public SoundSampleInfo Sample { get; } = sample;
        public short[] Pcm { get; } = pcm;
        public int Volume { get; } = volume;
        public int Pan { get; } = pan;
        public double Position;
        public bool Active = true;
    }

    private sealed class Lfo(
        byte target,
        byte targetExtra,
        byte shape,
        ushort dutyCycle,
        ushort depth,
        ushort flags,
        ushort startOffset,
        uint stepSize)
    {
        public byte Target { get; } = target;
        public byte TargetExtra { get; } = targetExtra;
        public byte Shape { get; } = shape;
        public ushort DutyCycle { get; } = dutyCycle;
        public int OriginalDepth { get; } = depth;
        public int Depth = depth;
        public ushort Flags { get; } = flags;
        public uint OriginalStepSize { get; } = stepSize;
        public uint StepSize = stepSize;
        public uint NextStep = (uint)startOffset << 16;
        public int RandomValue;
        public int RandomHalf;
    }

    private sealed class PreviewRandom(uint seed)
    {
        private uint _state = seed == 0 ? 1u : seed;

        public int Next(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
                throw new InvalidDataException("989snd preview random range is invalid.");
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (int)(_state % (uint)exclusiveMaximum);
        }
    }
}
