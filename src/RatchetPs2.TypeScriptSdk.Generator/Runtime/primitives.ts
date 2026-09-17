import type { Boxed, Int64 } from './contracts.js';
export const StringCompatibility = {
    Contains(value: string, part: string, comparison: number): boolean {
        return value.includes(part);
    },
    Replace(value: string, oldValue: string, newValue: string, comparison: number): string {
        return value.split(oldValue).join(newValue);
    },
    IndexOf(value: string, part: number, comparison: number): number {
        return value.indexOf(String.fromCharCode(part));
    },
};
export const CharCompatibility = {
    ToLowerInvariant(value: number): number {
        return String.fromCharCode(value).toLowerCase().charCodeAt(0);
    },
};
export const CheckedArithmetic = {
    AddInt32(left: number, right: number): number {
        let value = left + right;
        if (value < -2147483648 || value > 2147483647)
            throw new System.OverflowException();
        return value | 0;
    },
    SubtractInt32(left: number, right: number): number {
        let value = left - right;
        if (value < -2147483648 || value > 2147483647)
            throw new System.OverflowException();
        return value | 0;
    },
    MultiplyInt32(left: number, right: number): number {
        let value = left * right;
        if (value < -2147483648 || value > 2147483647)
            throw new System.OverflowException();
        return value | 0;
    },
    AddUInt32(left: number, right: number): number {
        let value = left + right;
        if (value > 4294967295)
            throw new System.OverflowException();
        return value >>> 0;
    },
    MultiplyUInt32(left: number, right: number): number {
        let value = left * right;
        if (value > 4294967295)
            throw new System.OverflowException();
        return value >>> 0;
    },
    AddInt64(left: Int64, right: Int64): Int64 {
        return left.add(right, true);
    },
    SubtractInt64(left: Int64, right: Int64): Int64 {
        return left.sub(right, true);
    },
    AddUInt64(left: Int64, right: Int64): Int64 {
        return left.add(right, true);
    },
    SubtractUInt64(left: Int64, right: Int64): Int64 {
        return left.sub(right, true);
    },
    ToInt32(value: number): number {
        value = Transpose.unbox(value);
        if (value > 2147483647)
            throw new System.OverflowException();
        return value | 0;
    },
    ToInt16(value: number): number {
        if (!Number.isFinite(value))
            throw new System.OverflowException();
        return Transpose.Int.check(Transpose.Int.trunc(value), System.Int16);
    },
    SingleToInt32(value: number): number {
        if (!Number.isFinite(value))
            throw new System.OverflowException();
        return Transpose.Int.check(Transpose.Int.trunc(value), System.Int32);
    },
};
export const FloatMath = {
    Round(value: number): number {
        return Math.fround(value);
    },
    Add(left: number, right: number): number {
        return Math.fround(left + right);
    },
    Subtract(left: number, right: number): number {
        return Math.fround(left - right);
    },
    Multiply(left: number, right: number): number {
        return Math.fround(left * right);
    },
    Divide(left: number, right: number): number {
        return Math.fround(left / right);
    },
    Remainder(left: number, right: number): number {
        return Math.fround(left % right);
    },
};
export const ReadOnlyListCompatibility = {
    Value<T>(value: T | Boxed<T>): T {
        return value && typeof value === 'object' && '$boxed' in value
            ? Transpose.unbox(value)
            : value as T;
    },
};
export const SortedDictionary = {
    Compare(left: string | number, right: string | number): number {
        return (left < right ? -1 : (left > right ? 1 : 0));
    },
};
