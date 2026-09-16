import type { ByteBuffer } from './contracts.js';
export const ByteChecksums = {
    Crc32(chunkType: ByteBuffer, data: ByteBuffer, table: number[]): number {
        let crc = 0xffffffff;
        for (let i = 0; i < chunkType.length; i++)
            crc = ((crc >>> 8) ^ table[(crc ^ chunkType[i]) & 255]) >>> 0;
        for (let i = 0; i < data.length; i++)
            crc = ((crc >>> 8) ^ table[(crc ^ data[i]) & 255]) >>> 0;
        return (~crc) >>> 0;
    },
    Adler32(data: ByteBuffer): number {
        let a = 1, b = 0;
        for (let i = 0; i < data.length; i++) {
            a = (a + data[i]) % 65521;
            b = (b + a) % 65521;
        }
        return ((b << 16) | a) >>> 0;
    },
};
export const ByteTextures = {
    DecodeIndexed8(pixels: ByteBuffer, palette: ByteBuffer, width: number, pixelCount: number, swizzled: boolean, decodePaletteIndexes: boolean): Uint8Array {
        const rgba = new Uint8Array(pixelCount * 4);
        for (let i = 0; i < pixelCount; i++) {
            let paletteIndex = pixels[i];
            if (decodePaletteIndexes)
                paletteIndex = (((paletteIndex & 16) >>> 1) | ((paletteIndex & 8) << 1) | (paletteIndex & 231)) & 255;
            let pixelIndex = i;
            if (swizzled) {
                let s = Math.floor(i / (width * 2));
                let r = s % 2 === 0 ? s * 2 : (s - 1) * 2 + 1;
                let q = Math.floor((i % (width * 2)) / 32);
                let m = i % 4;
                let n = Math.floor(i / 4) % 4;
                let o = i % 2;
                let p = Math.floor(i / 16) % 2;
                if (Math.floor(s / 2) % 2 === 1)
                    p = 1 - p;
                m = o === 0 ? (m + p) % 4 : (m - p + 4) % 4;
                let x = n + (m + q * 4) * 4;
                let y = r + o * 2;
                pixelIndex = x % width + y * width;
            }
            let paletteOffset = paletteIndex * 4;
            if (paletteOffset + 3 >= palette.length)
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
