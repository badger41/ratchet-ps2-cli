import type { ByteBuffer } from './contracts.js';
const numberBytes = new ArrayBuffer(4);
const numberView = new DataView(numberBytes);
const requireNumberBytes = (value: ByteBuffer, start: number, length: number) => {
    if (value == null)
        throw new globalThis.System.ArgumentNullException.$ctor1('value');
    if (start < 0 || start >= value.length)
        throw new globalThis.System.ArgumentOutOfRangeException.$ctor1('startIndex');
    if (start > value.length - length)
        throw new globalThis.System.ArgumentException.$ctor3('The array is too small.', 'value');
};
export const BitConverterCompatibility = {
    Int32BitsToSingle(value: number): number {
        numberView.setInt32(0, value, true);
        return Math.fround(numberView.getFloat32(0, true));
    },
    SingleToInt32Bits(value: number): number {
        numberView.setFloat32(0, value, true);
        return numberView.getInt32(0, true);
    },
    ToUInt16(value: ByteBuffer, start: number): number {
        requireNumberBytes(value, start, 2);
        return (value[start] | value[start + 1] << 8) >>> 0;
    },
    ToInt16(value: ByteBuffer, start: number): number {
        return this.ToUInt16(value, start) << 16 >> 16;
    },
    ToUInt32(value: ByteBuffer, start: number): number {
        requireNumberBytes(value, start, 4);
        return (value[start] | value[start + 1] << 8 | value[start + 2] << 16 | value[start + 3] << 24) >>> 0;
    },
    ToInt32(value: ByteBuffer, start: number): number {
        return this.ToUInt32(value, start) | 0;
    },
    ToSingle(value: ByteBuffer, start: number): number {
        requireNumberBytes(value, start, 4);
        numberView.setUint8(0, value[start]);
        numberView.setUint8(1, value[start + 1]);
        numberView.setUint8(2, value[start + 2]);
        numberView.setUint8(3, value[start + 3]);
        return Math.fround(numberView.getFloat32(0, true));
    }
};
