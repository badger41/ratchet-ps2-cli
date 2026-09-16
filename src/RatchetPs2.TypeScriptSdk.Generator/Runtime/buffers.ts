import type { ByteBuffer, Int64, WritableStream } from './contracts.js';
export const ByteArrays = {
    New(length: number): Uint8Array {
        return new Uint8Array(length);
    },
    NewInt64(length: Int64): Uint8Array {
        return new Uint8Array(length.toNumber());
    },
};
export const BrowserStreamExtensions = {
    Write(stream: WritableStream, buffer: ByteBuffer): void {
        stream.Write(buffer, 0, buffer.length);
    },
};
export const BufferCompatibility = {
    BlockCopy(source: ByteBuffer, sourceOffset: number, destination: ByteBuffer, destinationOffset: number, count: number): void {
        if (source == null || destination == null)
            throw new System.ArgumentNullException();
        if (sourceOffset < 0 || destinationOffset < 0 || count < 0 ||
            sourceOffset > source.length - count || destinationOffset > destination.length - count)
            throw new System.ArgumentException();
        let backing = source.__ratchetBytes || source;
        let start = sourceOffset + (source.__ratchetStart || 0);
        let copy = source instanceof Uint8Array
            ? source.subarray(sourceOffset, sourceOffset + count)
            : Uint8Array.from(backing.slice(start, start + count));
        if (destination instanceof Uint8Array)
            destination.set(copy, destinationOffset);
        else
            for (let i = 0; i < count; i++)
                destination[destinationOffset + i] = copy[i];
    },
};
export const ByteSpanExtensions = {
    CreateViews(): WeakSet<object> {
        return new WeakSet();
    },
    HasView(views: WeakSet<object>, value: object): boolean {
        return views.has(value);
    },
    RegisterView(views: WeakSet<object>, value: ByteBuffer): ByteBuffer {
        if (!(value instanceof Uint8Array))
            views.add(value);
        return value;
    },
    GetArray(bytes: ByteBuffer, index: number, fromEnd: boolean): number {
        let offset = fromEnd ? bytes.length - index : index;
        if (offset < 0 || offset >= bytes.length)
            throw new System.IndexOutOfRangeException();
        return bytes[offset];
    },
    Set(bytes: ByteBuffer, index: number, fromEnd: boolean, value: number): number {
        let offset = fromEnd ? bytes.length - index : index;
        if (offset < 0 || offset >= bytes.length)
            throw new System.IndexOutOfRangeException();
        bytes[offset] = value;
        return value;
    },
    NativeCopy(source: ByteBuffer, destination: ByteBuffer): void {
        let sourceBytes = source.__ratchetBytes;
        let sourceStart = source.__ratchetStart || 0;
        if (destination instanceof Uint8Array) {
            destination.set(sourceBytes
                ? sourceBytes.slice(sourceStart, sourceStart + source.length)
                : source);
            return;
        }
        let copy = Uint8Array.from(sourceBytes
            ? sourceBytes.slice(sourceStart, sourceStart + source.length)
            : source);
        for (let i = 0; i < copy.length; i++)
            destination[i] = copy[i];
    },
    NativeFill(bytes: ByteBuffer, value: number): void {
        if (bytes instanceof Uint8Array)
            bytes.fill(value);
        else if (bytes.__ratchetBytes)
            bytes.__ratchetBytes.fill(value, bytes.__ratchetStart ?? 0, (bytes.__ratchetStart ?? 0) + bytes.length);
        else
            for (let i = 0; i < bytes.length; i++)
                bytes[i] = value;
    },
    CreateView(bytes: ByteBuffer, start: number, length: number): ByteBuffer {
        if (bytes instanceof Uint8Array)
            return bytes.subarray(start, start + length);
        if (bytes.__ratchetBytes) {
            start += bytes.__ratchetStart ?? 0;
            bytes = bytes.__ratchetBytes;
        }
        // Ordinary arrays need an index proxy to keep span slices aliased. Native
        // byte arrays take the subarray path above; this cast describes the proxy.
        return new Proxy({}, {
            get: function (_, key) {
                if (key === 'length')
                    return length;
                if (key === '__ratchetBytes')
                    return bytes;
                if (key === '__ratchetStart')
                    return start;
                if (typeof key === 'string') {
                    let index = Number(key) >>> 0;
                    if (String(index) === key && index < length)
                        return bytes[start + index];
                }
            },
            set: function (_, key, value: number) {
                if (typeof key === 'string') {
                    let index = Number(key) >>> 0;
                    if (String(index) === key && index < length) {
                        bytes[start + index] = value & 255;
                        return true;
                    }
                }
                return false;
            }
        }) as ByteBuffer;
    },
    Writable(bytes: ByteBuffer): ByteBuffer {
        return bytes;
    },
    ReadOnly(bytes: ByteBuffer): ByteBuffer {
        return bytes;
    },
    AsReadOnly(bytes: ByteBuffer): ByteBuffer {
        return bytes;
    },
    Buffer(bytes: ByteBuffer): ByteBuffer {
        return bytes;
    },
    Array(bytes: ByteBuffer): ByteBuffer {
        return bytes;
    },
    At(bytes: ByteBuffer, index: number): number {
        return bytes[index];
    },
    CopyToArray(bytes: ByteBuffer): Uint8Array {
        let source = bytes.__ratchetBytes;
        let start = bytes.__ratchetStart || 0;
        return Uint8Array.from(source ? source.slice(start, start + bytes.length) : bytes);
    },
};
export const IntSpanExtensions = {
    Set(values: number[], index: number, value: number): number {
        return (values[index] = value);
    },
    Buffer(values: number[]): number[] {
        return values;
    },
};
export const ArrayCompatibility = {
    NewUninitialized<T>(length: number): T[] {
        return new Array(length);
    },
    Sort(array: number[]): void {
        array.sort(function (left, right) { return left - right; });
    },
};
export const BinaryPrimitives = {
    ReadUInt16LittleEndian(bytes: ByteBuffer): number {
        if (bytes.length < 2)
            throw new System.ArgumentOutOfRangeException('bytes');
        return (bytes[0] | bytes[1] << 8) >>> 0;
    },
    ReadUInt16BigEndian(bytes: ByteBuffer): number {
        if (bytes.length < 2)
            throw new System.ArgumentOutOfRangeException('bytes');
        return (bytes[0] << 8 | bytes[1]) >>> 0;
    },
    ReadUInt32LittleEndian(bytes: ByteBuffer): number {
        if (bytes.length < 4)
            throw new System.ArgumentOutOfRangeException('bytes');
        return (bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24) >>> 0;
    },
    ReadUInt32BigEndian(bytes: ByteBuffer): number {
        if (bytes.length < 4)
            throw new System.ArgumentOutOfRangeException('bytes');
        return (bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]) >>> 0;
    },
    At(bytes: ByteBuffer, index: number): number {
        return bytes[index];
    },
    Put(bytes: ByteBuffer, index: number, value: number): void {
        bytes[index] = value;
    },
};
