import { inflateSync, unzlibSync, unzipSync } from 'fflate';
import type { ByteBuffer, Int64 } from './contracts.js';
const zipValues = new WeakMap<ByteBuffer, Record<string, Uint8Array>>();
function unzip(bytes: ByteBuffer): Record<string, Uint8Array> {
    let value = zipValues.get(bytes);
    if (!value) {
        value = unzipSync(Uint8Array.from(bytes));
        zipValues.set(bytes, value);
    }
    return value;
}
export const BrowserCompression = {
    Unzlib(bytes: ByteBuffer): ByteBuffer {
        const value = Uint8Array.from(bytes);
        try {
            return unzlibSync(value);
        }
        catch (error) {
            const header = value[0] * 256 + value[1];
            if ((value[0] & 15) !== 8 || (value[0] >> 4) > 7 || header % 31 !== 0 || (value[1] & 32) !== 0)
                throw error;
            return inflateSync(value.subarray(2));
        }
    },
    ZipNames(bytes: ByteBuffer): string[] {
        return Object.keys(unzip(bytes));
    },
    ZipEntry(bytes: ByteBuffer, name: string): ByteBuffer {
        return unzip(bytes)[name];
    },
    Length(bytes: ByteBuffer): Int64 {
        return System.Int64(bytes.length);
    },
};
