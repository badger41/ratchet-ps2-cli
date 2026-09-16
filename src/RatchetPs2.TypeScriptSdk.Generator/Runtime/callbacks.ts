import type { ByteBuffer, MobyInput, MobyOutput } from './contracts.js';
export const BrowserMobyCallbacks = {
    FileExists(value: MobyInput, path: string): boolean {
        return value.fileExists(path);
    },
    DirectoryExists(value: MobyInput, path: string): boolean {
        return value.directoryExists(path);
    },
    ReadBytes(value: MobyInput, path: string): ByteBuffer {
        const result = value.readBytes(path);
        if (!(result instanceof Uint8Array))
            throw new TypeError('readBytes must return Uint8Array.');
        return Uint8Array.from(result);
    },
    EnumerateDirectories(value: MobyInput, path: string): string[] {
        const result = value.enumerateDirectories(path);
        if (!Array.isArray(result) || result.some(item => typeof item !== 'string'))
            throw new TypeError('enumerateDirectories must return strings.');
        return Array.from(result);
    },
    EnumerateFiles(value: MobyInput, path: string, pattern: string): string[] {
        const result = value.enumerateFiles(path, pattern);
        if (!Array.isArray(result) || result.some(item => typeof item !== 'string'))
            throw new TypeError('enumerateFiles must return strings.');
        return Array.from(result);
    },
    WriteBytes(value: MobyOutput, path: string, bytes: ByteBuffer): void {
        value.writeBytes(path, Uint8Array.from(bytes));
    },
};
