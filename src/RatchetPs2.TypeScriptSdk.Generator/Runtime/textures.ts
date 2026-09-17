import type { ByteBuffer } from './contracts.js';
let crcTableSource: number[] | undefined;
let crcTables: Uint32Array[] | undefined;
export const ByteChecksums = {
    Crc32(chunkType: ByteBuffer, data: ByteBuffer, table: number[]): number {
        if (crcTableSource !== table) {
            crcTableSource = table;
            crcTables = Array.from({ length: 8 }, () => new Uint32Array(256));
            crcTables[0].set(table);
            for (let slice = 1; slice < crcTables.length; slice++)
                for (let value = 0; value < 256; value++) {
                    const previous = crcTables[slice - 1][value];
                    crcTables[slice][value] = (previous >>> 8) ^ table[previous & 255];
                }
        }
        let crc = 0xffffffff;
        for (let i = 0; i < chunkType.length; i++)
            crc = ((crc >>> 8) ^ table[(crc ^ chunkType[i]) & 255]) >>> 0;
        let i = 0;
        const tables = crcTables!;
        for (; i + 8 <= data.length; i += 8) {
            crc ^= data[i] | data[i + 1] << 8 | data[i + 2] << 16 | data[i + 3] << 24;
            crc = (tables[7][crc & 255]
                ^ tables[6][crc >>> 8 & 255]
                ^ tables[5][crc >>> 16 & 255]
                ^ tables[4][crc >>> 24]
                ^ tables[3][data[i + 4]]
                ^ tables[2][data[i + 5]]
                ^ tables[1][data[i + 6]]
                ^ tables[0][data[i + 7]]) >>> 0;
        }
        for (; i < data.length; i++)
            crc = ((crc >>> 8) ^ table[(crc ^ data[i]) & 255]) >>> 0;
        return (~crc) >>> 0;
    },
    Adler32(data: ByteBuffer): number {
        let a = 1, b = 0;
        for (let offset = 0; offset < data.length;) {
            const end = Math.min(offset + 5552, data.length);
            for (; offset < end; offset++) {
                a += data[offset];
                b += a;
            }
            a %= 65521;
            b %= 65521;
        }
        return ((b << 16) | a) >>> 0;
    },
};
export const ByteTextures = {
    AnalyzeAlpha(pixels: ByteBuffer): Uint8Array {
        let min = 255, max = 0, binary = 1;
        for (let offset = 3; offset < pixels.length; offset += 4) {
            const alpha = pixels[offset];
            if (alpha < min) min = alpha;
            if (alpha > max) max = alpha;
            if (alpha !== 0 && alpha !== 255) binary = 0;
        }
        return new Uint8Array([min, max, binary]);
    },
    DecodeIndexed8(pixels: ByteBuffer, palette: ByteBuffer, width: number, pixelCount: number, swizzled: boolean, decodePaletteIndexes: boolean): Uint8Array {
        const rgba = new Uint8Array(pixelCount * 4);
        const checkPalette = palette.length < 1024;
        for (let i = 0; i < pixelCount; i++) {
            let paletteIndex = pixels[i];
            if (decodePaletteIndexes)
                paletteIndex = (((paletteIndex & 16) >>> 1) | ((paletteIndex & 8) << 1) | (paletteIndex & 231)) & 255;
            let pixelIndex = i;
            if (swizzled) {
                let s = (i / (width * 2)) | 0;
                let r = s % 2 === 0 ? s * 2 : (s - 1) * 2 + 1;
                let q = ((i % (width * 2)) / 32) | 0;
                let m = i & 3;
                let n = (i >>> 2) & 3;
                let o = i & 1;
                let p = (i >>> 4) & 1;
                if (((s >>> 1) & 1) === 1)
                    p = 1 - p;
                m = o === 0 ? (m + p) % 4 : (m - p + 4) % 4;
                let x = n + (m + q * 4) * 4;
                let y = r + o * 2;
                pixelIndex = x % width + y * width;
            }
            let paletteOffset = paletteIndex * 4;
            if (checkPalette && paletteOffset + 3 >= palette.length)
                throw new RatchetPs2.JavaScript.InvalidDataException('Palette index ' + paletteIndex + ' is outside the palette bounds.');
            let outputOffset = pixelIndex * 4;
            rgba[outputOffset] = palette[paletteOffset];
            rgba[outputOffset + 1] = palette[paletteOffset + 1];
            rgba[outputOffset + 2] = palette[paletteOffset + 2];
            rgba[outputOffset + 3] = palette[paletteOffset + 3];
        }
        return rgba;
    },
};
