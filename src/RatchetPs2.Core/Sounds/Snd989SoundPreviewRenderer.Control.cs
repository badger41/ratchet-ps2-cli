using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Sounds;

internal static partial class Snd989SoundPreviewRenderer
{
    private sealed partial class Engine
    {
        private void BeginSelection(Handler handler, SoundOperationInfo operation, bool random)
        {
            if (handler.Skipping || operation.Argument0 <= 0 || operation.Argument1 < 0)
                throw new InvalidDataException("989snd preview has an invalid grain selection.");
            var key = (handler.Program.ContentId
                ?? throw new InvalidDataException("989snd preview program has no content identity"),
                operation.NativeIndex);
            var previous = PreviewSelectionValues.GetOrAdd(key, operation.Argument2);
            var selection = random ? _random.Next(operation.Argument0) : previous;
            if (random && selection == previous)
                selection = (selection + 1) % operation.Argument0;
            PreviewSelectionValues[key] = random
                ? selection
                : (selection + 1) % operation.Argument0;
            handler.NextGrain += selection * operation.Argument1;
            handler.GrainsToPlay = operation.Argument1 + 1;
            handler.GrainsToSkip = (operation.Argument0 - selection - 1) * operation.Argument1;
            handler.Skipping = true;
        }

        private void ConfigureLfo(Handler handler, byte[] bytes)
        {
            var index = bytes[0];
            if (index >= handler.Lfos.Length) throw new InvalidDataException("989snd preview LFO index is invalid.");
            var target = bytes[1];
            if (target == 0)
            {
                handler.Lfos[index] = null;
                return;
            }
            var value = new Lfo(
                target, bytes[2], bytes[3],
                ReadUInt16LittleEndian(bytes, 4),
                ReadUInt16LittleEndian(bytes, 6),
                ReadUInt16LittleEndian(bytes, 8),
                ReadUInt16LittleEndian(bytes, 10),
                ReadUInt32LittleEndian(bytes, 12));
            if ((value.Flags & 2) != 0) value.NextStep = (uint)_random.Next(0x800) << 16;
            if (value.Shape == 5)
            {
                value.RandomValue = _random.Next(0x8000) * (_random.Next(2) * -1);
                value.RandomHalf = 1;
            }
            handler.Lfos[index] = value;
            ApplyLfo(handler, value);
        }

        private void UpdateLfos(Handler handler)
        {
            foreach (var lfo in handler.Lfos)
                if (lfo is not null) ApplyLfo(handler, lfo);
        }

        private void ApplyLfo(Handler handler, Lfo lfo)
        {
            var step = (int)(lfo.NextStep >> 16) & 0x7ff;
            lfo.NextStep = (lfo.NextStep + lfo.StepSize * 2) & 0x07ffffff;
            var wave = lfo.Shape switch
            {
                1 => (int)Math.Round(Math.Cos(step * Math.PI / 1024d) * 32767),
                2 => step < lfo.DutyCycle ? 32767 : -32767,
                3 => step < 512 ? step * 32767 / 512
                    : step < 1536 ? 32767 - (step - 512) * 65534 / 1024
                    : (step - 1536) * 32767 / 512 - 32767,
                4 => step < 1024 ? 32767 * step / 1023 : 32767 * (step - 1024) / 1024 - 32767,
                5 => RandomLfo(lfo, step),
                _ => 0,
            };
            if ((lfo.Flags & 1) != 0) wave *= -1;
            switch (lfo.Target)
            {
                case 1:
                    handler.LfoVolume = (handler.OriginalVolume * lfo.Depth >> 10) * (wave - 32767) >> 16;
                    break;
                case 2:
                    handler.LfoPan = (180 * lfo.Depth >> 10) * wave >> 15;
                    break;
                case 3:
                    handler.LfoPitchMod = (6096 * lfo.Depth >> 10) * wave >> 15;
                    break;
                case 4:
                    handler.LfoPitchBend = (32767 * lfo.Depth >> 10) * wave >> 15;
                    break;
                case 5 when lfo.TargetExtra < handler.Lfos.Length
                    && handler.Lfos[lfo.TargetExtra] is { } frequencyTarget:
                    var frequencyRange = (frequencyTarget.OriginalStepSize >> 8) * lfo.Depth >> 10;
                    frequencyTarget.StepSize = unchecked(frequencyTarget.OriginalStepSize
                        + (uint)((frequencyRange * wave >> 15) << 8));
                    break;
                case 6 when lfo.TargetExtra < handler.Lfos.Length
                    && handler.Lfos[lfo.TargetExtra] is { } depthTarget:
                    var depthRange = depthTarget.OriginalDepth * lfo.Depth >> 10;
                    depthTarget.Depth = depthTarget.OriginalDepth + (depthRange * wave >> 15);
                    break;
            }
        }

        private int RandomLfo(Lfo lfo, int step)
        {
            if (step >= 0x400 && lfo.RandomHalf == 1)
            {
                lfo.RandomHalf = 0;
                lfo.RandomValue = 2 * (_random.Next(0x8000) - 0x3fff);
            }
            else if (step < 0x400 && lfo.RandomHalf == 0)
            {
                lfo.RandomHalf = 1;
                lfo.RandomValue = _random.Next(0x8000) * (_random.Next(2) * -1);
            }
            return lfo.RandomValue;
        }

        private void StartChild(Handler handler, SoundOperationInfo operation)
        {
            if (operation.ReferencedProgramIndex is not { } index) return;
            var bytes = operation.ParameterBytes;
            var volume = ResolveParameter(handler, ReadInt32LittleEndian(bytes, 0), false);
            var child = StartHandler(
                _inventory.Programs[index], handler,
                handler.AppVolume * handler.OriginalVolume / 127,
                Math.Clamp(Math.Abs(volume), 0, 127),
                handler.AppPitchMod,
                handler.AppPitchBend);
            Array.Copy(handler.Registers, child.Registers, child.Registers.Length);
            RunReady(child);
        }

        private void StopChildren(Handler handler, int? programIndex)
        {
            if (programIndex is null) return;
            foreach (var child in _handlers.Where(value => value.Parent == handler
                && value.Program.NativeIndex == programIndex).ToArray()) StopHandler(child);
        }

        private void Branch(Handler handler, SoundOperationInfo operation)
        {
            if (operation.ReferencedProgramIndex is not { } index)
                throw new NotSupportedException("Named cross-bank branches are not supported for preview.");
            foreach (var voice in _voices.Where(value => value.Owner == handler)) voice.Active = false;
            handler.Program = _inventory.Programs[index];
            handler.OriginalVolume = handler.Program.Volume;
            handler.NextGrain = -1;
            handler.Skipping = false;
        }

        private void StopHandler(Handler handler)
        {
            handler.Stopped = true;
            foreach (var voice in _voices.Where(value => value.Owner == handler)) voice.Active = false;
            foreach (var child in _handlers.Where(value => value.Parent == handler).ToArray()) StopHandler(child);
        }

        private int FindPrevious(Handler handler, byte type)
        {
            for (var index = handler.NextGrain - 1; index >= 0; index--)
                if (handler.Program.Operations[index].Type == type) return index;
            throw new InvalidDataException("989snd preview loop start is missing.");
        }

        private int FindNext(Handler handler, byte type)
        {
            for (var index = handler.NextGrain + 1; index < handler.Program.Operations.Count; index++)
                if (handler.Program.Operations[index].Type == type) return index;
            throw new InvalidDataException("989snd preview loop end is missing.");
        }

        private static int FindMarker(Handler handler, sbyte marker)
        {
            for (var index = 0; index < handler.Program.Operations.Count; index++)
                if (handler.Program.Operations[index] is { Type: 0x23 } operation
                    && operation.Argument0 == marker) return index;
            throw new InvalidDataException("989snd preview marker is missing.");
        }

        private sbyte ReadRegister(Handler handler, sbyte selector) => selector >= 0
            ? handler.Registers[selector]
            : _globalRegisters[-selector - 1];

        private void WriteRegister(Handler handler, sbyte selector, sbyte value)
        {
            if (selector >= 0) handler.Registers[selector] = value;
            else _globalRegisters[-selector - 1] = value;
        }

        private int ResolveParameter(Handler handler, int value, bool pan)
        {
            if (value >= 0) return value;
            var resolved = value switch
            {
                >= -4 => handler.Registers[-value - 1],
                -5 => _random.Next(pan ? 360 : 128),
                _ => _globalRegisters[-value - 6],
            };
            return pan ? 360 * Math.Abs(resolved) / 127 : resolved;
        }
    }
}
