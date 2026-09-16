import type { ByteBuffer } from './contracts.js';
import { serializeBytes, serialize, clone } from './json.js';
export const BrowserJson = {
    Parse(bytes: ByteBuffer): unknown {
        return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Uint8Array.from(bytes)));
    },
    SerializeBytes(value: unknown, indented: boolean, stringEnums: boolean): ByteBuffer {
        return serializeBytes(value, indented, stringEnums);
    },
    Serialize(value: unknown, indented: boolean, stringEnums: boolean): string {
        return serialize(value, indented, stringEnums);
    },
    Clone(value: unknown): unknown {
        return clone(value);
    },
    NewObject(): Record<string, unknown> {
        return Object.create(null);
    },
    NewArray(): unknown[] {
        return [];
    },
    Raw(value: number | boolean): unknown {
        return value;
    },
    Value<T>(value: unknown): T {
        return Transpose.unbox(value) as T;
    },
    Kind(value: unknown): number {
        if (value === null)
            return 7;
        if (Array.isArray(value))
            return 2;
        switch (typeof value) {
            case 'object': return 1;
            case 'string': return 3;
            case 'number': return 4;
            case 'boolean': return value ? 5 : 6;
            default: return 0;
        }
    },
    HasProperty(value: unknown, name: string): boolean {
        if (value === null || Array.isArray(value) || typeof value !== 'object')
            throw new TypeError('Expected a JSON object.');
        return Object.prototype.hasOwnProperty.call(value, name);
    },
    Property(value: Record<string, unknown>, name: string): unknown {
        return value[name];
    },
    SetProperty(target: Record<string, unknown>, name: string, value: unknown): void {
        target[name] = Transpose.unbox(value);
    },
    RemoveProperty(target: Record<string, unknown>, name: string): boolean {
        return delete target[name];
    },
    Add(target: unknown[], value: unknown): void {
        target.push(Transpose.unbox(value));
    },
    ArrayItem(value: unknown, index: number): unknown {
        if (!Array.isArray(value) || index < 0 || index >= value.length)
            throw new RangeError('JSON array index is out of range.');
        return value[index];
    },
    ArrayLength(value: unknown): number {
        if (!Array.isArray(value))
            throw new TypeError('Expected a JSON array.');
        return value.length;
    },
    Array(value: unknown): unknown[] {
        if (!Array.isArray(value))
            throw new TypeError('Expected a JSON array.');
        return value;
    },
    Keys(value: unknown): string[] {
        if (value === null || Array.isArray(value) || typeof value !== 'object')
            throw new TypeError('Expected a JSON object.');
        return Object.keys(value);
    },
    Int32(value: unknown): number {
        if (typeof value !== 'number' || !Number.isInteger(value) || value < -2147483648 || value > 2147483647)
            throw new TypeError('Expected a 32-bit JSON integer.');
        return value;
    },
    Single(value: unknown): number {
        if (typeof value !== 'number')
            throw new TypeError('Expected a JSON number.');
        return Math.fround(value);
    },
    String(value: unknown): string | null {
        if (value !== null && typeof value !== 'string')
            throw new TypeError('Expected a JSON string.');
        return value;
    },
    Boolean(value: unknown): boolean {
        if (typeof value !== 'boolean')
            throw new TypeError('Expected a JSON boolean.');
        return value;
    },
};
