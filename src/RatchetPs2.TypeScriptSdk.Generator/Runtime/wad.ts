import type { ByteBuffer } from './contracts.js';
export const BrowserWad = {
    Decompress(source: ByteBuffer): Uint8Array {
        if (source == null)
            throw new System.ArgumentNullException.$ctor1('source');
        if (source.length < 16)
            throw new RatchetPs2.JavaScript.InvalidDataException('Input is too small to contain a valid WAD header.');
        if (source[0] !== 87 || source[1] !== 65 || source[2] !== 68)
            throw new RatchetPs2.JavaScript.InvalidDataException('Input is not a valid compressed WAD file because it does not start with WAD magic.');
        let end = (source[3] | source[4] << 8 | source[5] << 16 | source[6] << 24) | 0;
        if (end <= 0 || end > source.length)
            throw new RatchetPs2.JavaScript.InvalidDataException('Compressed WAD size is invalid.');
        let cursor = 16, payloadStart = 16, length = 0;
        let destination = new Uint8Array(Math.max(256, end * 2));
        function ensure(count: number) {
            if (length + count <= destination.length)
                return;
            let next = new Uint8Array(Math.max(length + count, destination.length * 2));
            next.set(destination.subarray(0, length));
            destination = next;
        }
        function read8() {
            if (cursor >= end || cursor < payloadStart)
                throw new RatchetPs2.JavaScript.InvalidDataException('Unexpected end of compressed WAD buffer.');
            return source[cursor++];
        }
        function literal(count: number) {
            if (cursor + count > end || cursor < payloadStart)
                throw new RatchetPs2.JavaScript.InvalidDataException('Unexpected end of compressed WAD buffer.');
            ensure(count);
            for (let i = 0; i < count; i++)
                destination[length++] = source[cursor + i];
            cursor += count;
        }
        function match(offset: number, count: number) {
            if (count === 1)
                return;
            if (offset < 0 || offset >= length)
                throw new RatchetPs2.JavaScript.InvalidDataException('Match packet points outside of the decompressed buffer.');
            ensure(count);
            for (let i = 0; i < count; i++)
                destination[length++] = destination[offset + i];
        }
        while (cursor < end) {
            let flag = read8();
            if (flag < 16) {
                literal(flag !== 0 ? flag + 3 : read8() + 18);
                if (cursor < end && source[cursor] < 16)
                    throw new RatchetPs2.JavaScript.InvalidDataException('Unexpected double literal in compressed WAD stream.');
                continue;
            }
            let matchLength = 0, lookbackOffset = -1;
            if (flag < 32) {
                matchLength = flag & 7;
                if (matchLength === 0)
                    matchLength = read8() + 7;
                let low = read8(), high = read8();
                lookbackOffset = length - ((flag & 8) * 2048) - high * 64 - (low >>> 2);
                if (lookbackOffset === length && matchLength !== 1) {
                    while ((cursor - payloadStart) % 4096 !== 0) {
                        if (++cursor > end)
                            throw new RatchetPs2.JavaScript.InvalidDataException('Compressed WAD padding stepped outside the buffer.');
                    }
                    continue;
                }
                if (lookbackOffset !== length) {
                    matchLength += 2;
                    lookbackOffset -= 16384;
                }
            }
            else if (flag < 64) {
                matchLength = flag & 31;
                if (matchLength === 0)
                    matchLength = read8() + 31;
                matchLength += 2;
                let low = read8(), high = read8();
                lookbackOffset = length - high * 64 - (low >>> 2) - 1;
            }
            else {
                let major = read8();
                lookbackOffset = length - major * 8 - ((flag >>> 2) & 7) - 1;
                matchLength = (flag >>> 5) + 1;
            }
            match(lookbackOffset, matchLength);
            literal(source[cursor - 2] & 3);
        }
        return destination.slice(0, length);
    },
};
