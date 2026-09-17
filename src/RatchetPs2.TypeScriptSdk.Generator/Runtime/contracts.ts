// Structural contracts for the generated Transpose runtime. Keep this boundary
// explicit: browser implementations must not turn off TypeScript's strict checks.
export type ByteBuffer = (Uint8Array | number[]) & {
    __ratchetBytes?: number[];
    __ratchetStart?: number;
};
export interface Int64 {
    toNumber(): number;
    toString(): string;
    add(value: Int64, checked: boolean): Int64;
    sub(value: Int64, checked: boolean): Int64;
}
export interface MobyInput {
    fileExists(path: string): boolean;
    directoryExists(path: string): boolean;
    readBytes(path: string): unknown;
    enumerateDirectories(path: string): unknown;
    enumerateFiles(path: string, pattern: string): unknown;
}
export interface MobyOutput {
    writeBytes(path: string, bytes: Uint8Array): void;
}
export interface WritableStream {
    Write(buffer: ByteBuffer, offset: number, count: number): void;
}
export interface MemoryStream extends WritableStream {
    _buffer: ByteBuffer;
    _capacity: number;
    _length: number;
    _position: number;
    _origin: number;
    _isOpen: boolean;
    EnsureCapacity(value: number): boolean;
    EnsureWriteable(): void;
    WriteByte(value: number): void;
    ToArray(): Uint8Array;
}
export interface Boxed<T = unknown> {
    $boxed: true;
    v: T;
    type?: {
        $kind?: string;
    };
    toString(): string;
}
interface ExceptionConstructor {
    new (message?: string): Error;
    $ctor1: new (message: string) => Error;
    $ctor3: new (parameter: string, message: string) => Error;
    $ctor4: new (parameter: string, message: string) => Error;
}
declare global {
    var System: {
        ArgumentException: ExceptionConstructor;
        ArgumentNullException: ExceptionConstructor;
        ArgumentOutOfRangeException: ExceptionConstructor;
        IndexOutOfRangeException: ExceptionConstructor;
        OverflowException: ExceptionConstructor;
        Int16: unknown;
        Int32: unknown;
        Int64(value: string | number): Int64;
        Array: {
            copy(source: unknown[] | Uint8Array, sourceOffset: number, destination: unknown[] | Uint8Array, destinationOffset: number, count: number): void;
            getCount(value: {
                _items?: unknown[];
                _size?: number;
            }, type?: unknown): number;
            type(elementType: unknown, rank?: number, array?: unknown[]): unknown;
        };
        IO: {
            IOException: ExceptionConstructor;
            __Error: {
                StreamIsClosed(): never;
            };
            MemoryStream: {
                prototype: MemoryStream;
            };
        };
        Linq: {
            Enumerable: {
                from(value: object): {
                    ToArray(): unknown[];
                };
            };
        };
        Numerics: {
            Vector3: {
                prototype: {
                    X: number;
                    Y: number;
                    Z: number;
                    $clone(to?: { X: number; Y: number; Z: number }): { X: number; Y: number; Z: number };
                };
            };
        };
    };
    var Transpose: {
        unbox<T>(value: T | Boxed<T>, noClone?: boolean): T;
        getHashCode(value: unknown, safe?: boolean, deep?: boolean): number;
        Int: {
            check(value: number, type: unknown): number;
            trunc(value: number): number;
        };
    };
    var TransposeR: {
        array(length: number, value: unknown): unknown[];
    };
    var RatchetPs2: {
        JavaScript: {
            InvalidDataException: new (message: string) => Error;
            GeneratedExports: {
                CreateMemoryStream(bytes: Uint8Array): MemoryStream;
                StreamPosition(value: MemoryStream): Int64;
                StreamLength(value: MemoryStream): Int64;
                StreamSeek(value: MemoryStream, offset: Int64, origin: number): Int64;
                StreamBytes(value: MemoryStream): ByteBuffer;
            };
        };
    };
}
