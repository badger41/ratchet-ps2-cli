import type { MemoryStream } from './contracts.js';
export function installRuntime(): void {
    const originalArrayCopy = globalThis.System.Array.copy;
    globalThis.System.Array.copy = (source, sourceOffset, destination, destinationOffset, length) => {
        if (source == null || destination == null || sourceOffset < 0 || destinationOffset < 0 || length < 0 ||
            sourceOffset > source.length - length || destinationOffset > destination.length - length)
            return originalArrayCopy(source, sourceOffset, destination, destinationOffset, length);
        if (destination instanceof Uint8Array && (source instanceof Uint8Array || Array.isArray(source))) {
            destination.set(source.slice(sourceOffset, sourceOffset + length) as number[] | Uint8Array, destinationOffset);
            return;
        }
        if ((Array.isArray(source) || source instanceof Uint8Array) && Array.isArray(destination)) {
            const values = source.slice(sourceOffset, sourceOffset + length);
            for (let offset = 0; offset < length; offset += 8192) {
                const count = Math.min(8192, length - offset);
                destination.splice(destinationOffset + offset, count, ...values.slice(offset, offset + count));
            }
            return;
        }
        originalArrayCopy(source, sourceOffset, destination, destinationOffset, length);
    };
    const originalArrayCount = globalThis.System.Array.getCount;
    globalThis.System.Array.getCount = (value, type) => value && Array.isArray(value._items) && Number.isInteger(value._size)
        ? value._size!
        : originalArrayCount(value, type);
    const originalRuntimeArray = globalThis.TransposeR.array;
    globalThis.TransposeR.array = (length, value) => typeof value !== 'function' && (value === null || typeof value !== 'object')
        ? new Array(length).fill(value)
        : originalRuntimeArray(length, value);
    globalThis.System.IO.MemoryStream.prototype.EnsureCapacity = function (value) {
        if (value < 0)
            throw new globalThis.System.IO.IOException.$ctor1('IO.IO_StreamTooLong');
        if (value <= this._capacity)
            return false;
        let capacity = Math.max(value, 256, this._capacity * 2);
        if (this._capacity * 2 > 2147483591)
            capacity = value > 2147483591 ? value : 2147483591;
        const buffer = new Uint8Array(capacity);
        if (this._length > 0)
            buffer.set(this._buffer.slice(0, this._length));
        this._buffer = buffer;
        this._capacity = capacity;
        return true;
    };
    globalThis.System.IO.MemoryStream.prototype.Write = function (buffer, offset, count) {
        if (buffer == null)
            throw new globalThis.System.ArgumentNullException.$ctor3('buffer', 'ArgumentNull_Buffer');
        if (offset < 0)
            throw new globalThis.System.ArgumentOutOfRangeException.$ctor4('offset', 'ArgumentOutOfRange_NeedNonNegNum');
        if (count < 0)
            throw new globalThis.System.ArgumentOutOfRangeException.$ctor4('count', 'ArgumentOutOfRange_NeedNonNegNum');
        if (buffer.length - offset < count)
            throw new globalThis.System.ArgumentException.$ctor1('Argument_InvalidOffLen');
        if (!this._isOpen)
            globalThis.System.IO.__Error.StreamIsClosed();
        this.EnsureWriteable();
        const end = this._position + count;
        if (end < 0)
            throw new globalThis.System.IO.IOException.$ctor1('IO.IO_StreamTooLong');
        if (end > this._length) {
            const mustZero = this._position > this._length && end <= this._capacity;
            if (end > this._capacity)
                this.EnsureCapacity(end);
            if (mustZero)
                this._buffer.fill(0, this._length, this._position);
            this._length = end;
        }
        const source = buffer.__ratchetBytes;
        const start = (buffer.__ratchetStart || 0) + offset;
        if (this._buffer instanceof Uint8Array) {
            const values = source ? source.slice(start, start + count)
                : buffer instanceof Uint8Array ? buffer.subarray(offset, offset + count)
                    : buffer.slice(offset, offset + count);
            this._buffer.set(values, this._position);
        }
        else {
            const values = Array.from(source ? source.slice(start, start + count) : buffer.slice(offset, offset + count));
            for (let index = 0; index < count; index++)
                this._buffer[this._position + index] = values[index];
        }
        this._position = end;
    };
    globalThis.System.IO.MemoryStream.prototype.WriteByte = function (value) {
        if (!this._isOpen)
            globalThis.System.IO.__Error.StreamIsClosed();
        this.EnsureWriteable();
        if (this._position >= this._length) {
            const end = this._position + 1;
            const mustZero = this._position > this._length && end < this._capacity;
            if (end >= this._capacity)
                this.EnsureCapacity(end);
            if (mustZero)
                this._buffer.fill(0, this._length, this._position);
            this._length = end;
        }
        this._buffer[this._position++] = value & 255;
    };
    globalThis.System.IO.MemoryStream.prototype.ToArray = function () {
        const bytes = this._buffer.slice(this._origin, this._length);
        return bytes instanceof Uint8Array ? bytes : Uint8Array.from(bytes);
    };
}
export const nativeValues = new WeakMap<object, unknown>();
export function retainNative<T extends object>(projected: T, value: unknown): T { nativeValues.set(projected, value); return projected; }
export function projectMemoryStream(value: MemoryStream) {
    return retainNative({
        get position() { return BigInt(globalThis.RatchetPs2.JavaScript.GeneratedExports.StreamPosition(value).toString()); },
        get length() { return BigInt(globalThis.RatchetPs2.JavaScript.GeneratedExports.StreamLength(value).toString()); },
        seek(offset: bigint, origin = 0) {
            if (typeof offset !== 'bigint' || !Number.isInteger(origin) || origin < 0 || origin > 2)
                throw new TypeError('Invalid stream seek.');
            return BigInt(globalThis.RatchetPs2.JavaScript.GeneratedExports.StreamSeek(value, globalThis.System.Int64(offset.toString()), origin).toString());
        },
        toArray() { return Uint8Array.from(globalThis.RatchetPs2.JavaScript.GeneratedExports.StreamBytes(value)); }
    }, value);
}
export function createMemoryStream(bytes: Uint8Array | ArrayBuffer = new Uint8Array()) {
    if (bytes instanceof ArrayBuffer)
        bytes = new Uint8Array(bytes);
    if (!(bytes instanceof Uint8Array))
        throw new TypeError('Expected a binary buffer.');
    return projectMemoryStream(globalThis.RatchetPs2.JavaScript.GeneratedExports.CreateMemoryStream(bytes));
}
