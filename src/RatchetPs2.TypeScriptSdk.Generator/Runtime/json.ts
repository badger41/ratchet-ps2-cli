import type { Boxed } from './contracts.js';
export type JsonValue = null | boolean | number | string | JsonValue[] | {
    [key: string]: JsonValue;
};
interface RuntimeJsonObject {
    [key: string]: unknown;
    $boxed?: boolean;
    v?: unknown;
    type?: {
        $kind?: string;
    };
    $type?: {
        $$name?: string;
    };
    $$name?: string;
    $$anonFormat?: Record<string, (value: unknown) => string>;
    entries?: {
        hashCode: number;
        key: unknown;
        value: unknown;
    }[];
    count$1?: number;
    GetEnumerator?: () => unknown;
    Export(): unknown;
    toJSON?: () => unknown;
}
let jsonIgnoredMembers = new Set<string>();
const utf8 = new TextEncoder();
export function configureJson(ignoredMembers: string[]): void {
    jsonIgnoredMembers = new Set(ignoredMembers);
}
function base64(bytes: Uint8Array): string {
    let value = '';
    for (let offset = 0; offset < bytes.length; offset += 0x8000)
        value += String.fromCharCode(...bytes.subarray(offset, Math.min(offset + 0x8000, bytes.length)));
    return btoa(value);
}
export function jsonValue(value: unknown, stringEnums: boolean, seen = new Set<object>()): JsonValue {
    if (value === null || typeof value === 'string' || typeof value === 'boolean')
        return value;
    if (typeof value === 'number') {
        if (!Number.isFinite(value))
            throw new TypeError('JSON cannot serialize non-finite numbers.');
        return value;
    }
    if (typeof value === 'bigint')
        return Number(value);
    if (typeof value !== 'object')
        throw new TypeError('Expected a JSON value.');
    // Transpose models carry metadata and methods; only this interop boundary
    // interprets them. Ordinary JSON stays unknown until validated below.
    const runtime = value as RuntimeJsonObject;
    if (runtime.$boxed)
        return stringEnums && runtime.type?.$kind === 'enum' ? runtime.toString() : jsonValue(runtime.v, stringEnums, seen);
    if (value instanceof Uint8Array || runtime.$type?.$$name === 'System.Byte[]')
        return base64(Uint8Array.from(value as ArrayLike<number>));
    if (seen.has(value))
        throw new TypeError('JSON cannot serialize a cyclic value.');
    seen.add(value);
    try {
        if (runtime.$$name?.startsWith('RatchetPs2.JavaScript.Json'))
            return jsonValue(runtime.Export(), stringEnums, seen);
        if (Array.isArray(value))
            return Array.from(value, item => jsonValue(item, stringEnums, seen));
        const typeName = (value.constructor as {
            $$fullname?: string;
        } | undefined)?.$$fullname ?? runtime.$$name;
        if (typeName?.startsWith('System.Collections.Generic.Dictionary`2')) {
            const result: Record<string, JsonValue> = {};
            for (const entry of (runtime.entries ?? []).slice(0, runtime.count$1)) {
                const key = entry.key as Partial<Boxed> | null;
                if (entry.hashCode >= 0)
                    result[String(key?.$boxed ? key.v : entry.key)] = jsonValue(entry.value, stringEnums, seen);
            }
            return result;
        }
        if (typeof runtime.GetEnumerator === 'function')
            return Array.from(System.Linq.Enumerable.from(value).ToArray(), item => jsonValue(item, stringEnums, seen));
        const source = runtime.$$anonFormat || typeof runtime.toJSON !== 'function' ? value : runtime.toJSON();
        if (source === null || typeof source !== 'object' || Array.isArray(source))
            return jsonValue(source, stringEnums, seen);
        const record = source as RuntimeJsonObject;
        const result: Record<string, JsonValue> = {};
        for (const key of Object.keys(record)) {
            if (jsonIgnoredMembers.has(typeName + ':' + key))
                continue;
            const item = record[key];
            if (item === undefined || typeof item === 'function')
                continue;
            result[key] = stringEnums && typeof Transpose.unbox(item, true) !== 'boolean' && record.$$anonFormat?.[key]
                ? record.$$anonFormat[key](item)
                : jsonValue(item, stringEnums, seen);
        }
        return result;
    }
    finally {
        seen.delete(value);
    }
}
export function serialize(value: unknown, indented: boolean, stringEnums: boolean): string {
    return JSON.stringify(jsonValue(value, stringEnums), null, indented ? 2 : undefined);
}
export function serializeBytes(value: unknown, indented: boolean, stringEnums: boolean): Uint8Array {
    return utf8.encode(serialize(value, indented, stringEnums));
}
export function clone(value: unknown): JsonValue {
    return JSON.parse(JSON.stringify(jsonValue(value, false)));
}
export function normalizeJson(value: unknown): JsonValue {
    if (value === null || typeof value === 'boolean' || typeof value === 'string')
        return value;
    if (typeof value === 'number' && Number.isFinite(value))
        return value;
    if (Array.isArray(value))
        return Array.from(value, normalizeJson);
    if (typeof value === 'object' && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null)) {
        const result: Record<string, JsonValue> = Object.create(null);
        for (const key of Object.keys(value))
            result[key] = normalizeJson((value as Record<string, unknown>)[key]);
        return result;
    }
    throw new TypeError('Expected a JSON value.');
}
