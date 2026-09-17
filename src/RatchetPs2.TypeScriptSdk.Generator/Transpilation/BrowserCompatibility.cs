#if TRANSPOSE
// APIs missing from Transpose.BCL; normal .NET uses its own BCL.
// Templates are call bindings only. Implementations live in the strictly typed
// Runtime/*.ts modules imported by the generated ratchetps2.js entrypoint.
namespace RatchetPs2.JavaScript
{
    public static class ByteArrays
    {
        [Transpose.Template("Browser.ByteArrays.New({length})")]
        public static extern byte[] New(int length);

        [Transpose.Template("Browser.ByteArrays.New({length})")]
        public static extern byte[] New(uint length);

        [Transpose.Template("Browser.ByteArrays.NewInt64({length})")]
        public static extern byte[] New(long length);

        [Transpose.Template("Browser.ByteArrays.NewInt64({length})")]
        public static extern byte[] New(ulong length);
    }

    public static class ByteChecksums
    {
        [Transpose.Template("Browser.ByteChecksums.Crc32({chunkType}, {data}, {table})")]
        public static extern uint Crc32(byte[] chunkType, byte[] data, uint[] table);

        [Transpose.Template("Browser.ByteChecksums.Adler32({data})")]
        public static extern uint Adler32(byte[] data);
    }

    public static class ByteTextures
    {
        [Transpose.Template("Browser.ByteTextures.AnalyzeAlpha({pixels})")]
        public static extern byte[] AnalyzeAlpha(byte[] pixels);

        [Transpose.Template("Browser.ByteTextures.DecodeIndexed8({pixels}, {palette}, {width}, {pixelCount}, {swizzled}, {decodePaletteIndexes})")]
        public static extern byte[] DecodeIndexed8(
            byte[] pixels,
            byte[] palette,
            int width,
            int pixelCount,
            bool swizzled,
            bool decodePaletteIndexes);
    }

    public static class BrowserWad
    {
        [Transpose.Template("Browser.BrowserWad.Decompress({source})")]
        public static extern byte[] Decompress(byte[] source);
    }

    public static class Guards
    {
        public static void ThrowIfNull(object value, string parameterName)
        {
            if (value == null) throw new System.ArgumentNullException(parameterName);
        }

        public static void ThrowIfNullOrWhiteSpace(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new System.ArgumentException();
        }
    }

    public enum JsonValueKind
    {
        Undefined,
        Object,
        Array,
        String,
        Number,
        True,
        False,
        Null
    }

    public sealed class JsonDocument : System.IDisposable
    {
        private JsonDocument(object value) => RootElement = new JsonElement(value);

        public JsonElement RootElement { get; }

        public static JsonDocument Parse(byte[] bytes) => new(BrowserJson.Parse(bytes));
        public static JsonDocument Parse(System.ReadOnlyMemory<byte> bytes) => Parse(bytes.ToArray());
        public static JsonDocument Parse(System.IO.Stream input)
        {
            using var copy = new System.IO.MemoryStream();
            input.CopyTo(copy);
            return Parse(copy.ToArray());
        }

        public void Dispose() { }
    }

    public sealed class JsonElement
    {
        private readonly object value;

        public JsonElement(object value) => this.value = value;

        public JsonValueKind ValueKind => (JsonValueKind)BrowserJson.Kind(value);
        public JsonElement this[int index] => new(BrowserJson.ArrayItem(value, index));

        public bool TryGetProperty(string name, out JsonElement result)
        {
            if (BrowserJson.HasProperty(value, name))
            {
                result = new JsonElement(BrowserJson.Property(value, name));
                return true;
            }
            result = null;
            return false;
        }

        public JsonElement GetProperty(string name)
        {
            if (!TryGetProperty(name, out var result)) throw new System.Collections.Generic.KeyNotFoundException();
            return result;
        }

        public int GetArrayLength() => BrowserJson.ArrayLength(value);
        public int GetInt32() => BrowserJson.Int32(value);
        public float GetSingle() => BrowserJson.Single(value);
        public string GetString() => BrowserJson.String(value);
        public bool GetBoolean() => BrowserJson.Boolean(value);
        public JsonElement Clone() => this;
        public object Export() => value;

        public JsonElement[] EnumerateArray()
        {
            var values = BrowserJson.Array(value);
            var result = new JsonElement[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = new JsonElement(values[i]);
            return result;
        }

        public JsonProperty[] EnumerateObject()
        {
            var names = BrowserJson.Keys(value);
            var result = new JsonProperty[names.Length];
            for (var i = 0; i < names.Length; i++) result[i] = new JsonProperty(names[i], new JsonElement(BrowserJson.Property(value, names[i])));
            return result;
        }
    }

    public sealed class JsonProperty(string name, JsonElement value)
    {
        public string Name { get; } = name;
        public JsonElement Value { get; } = value;
    }

    public sealed class JsonSerializerOptions
    {
        public bool WriteIndented { get; set; }
        public System.Collections.Generic.List<object> Converters { get; } = new();
    }

    public sealed class JsonStringEnumConverter { }

    [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
    public sealed class JsonIgnoreAttribute : System.Attribute { }

    public static class JsonSerializer
    {
        public static byte[] SerializeToUtf8Bytes(object value) => SerializeToUtf8Bytes(value, null);
        public static byte[] SerializeToUtf8Bytes(object value, JsonSerializerOptions options) =>
            BrowserJson.SerializeBytes(value, options != null && options.WriteIndented, options != null && options.Converters.Count > 0);

        public static string Serialize(object value) => Serialize(value, null);
        public static string Serialize(object value, JsonSerializerOptions options) =>
            BrowserJson.Serialize(value, options != null && options.WriteIndented, options != null && options.Converters.Count > 0);

        public static void Serialize(System.IO.Stream output, object value, JsonSerializerOptions options)
        {
            var bytes = SerializeToUtf8Bytes(value, options);
            output.Write(bytes, 0, bytes.Length);
        }

        public static System.Collections.Generic.Dictionary<string, object> CloneDictionary(object value)
        {
            var raw = BrowserJson.Clone(value);
            var result = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var name in BrowserJson.Keys(raw)) result.Add(name, BrowserJson.Property(raw, name));
            return result;
        }
    }

    public abstract class JsonNode
    {
        private readonly object value;
        protected JsonNode(object value) => this.value = value;

        public virtual JsonNode this[string name] { get => null; set => throw new System.InvalidOperationException(); }
        public object Export() => value;
        public JsonObject AsObject() => this as JsonObject ?? throw new System.InvalidOperationException();
        public JsonNode DeepClone() => Wrap(BrowserJson.Clone(value));
        public T GetValue<T>() => BrowserJson.Value<T>(value);

        public static JsonNode Parse(byte[] bytes) => Wrap(BrowserJson.Parse(bytes));
        public static JsonNode Parse(System.ReadOnlySpan<byte> bytes) => Parse(System.ByteSpanExtensions.CopyToArray(bytes));
        internal static JsonNode Wrap(object value) => value == null ? null : BrowserJson.Kind(value) switch
        {
            1 => new JsonObject(value),
            2 => new JsonArray(value),
            _ => new JsonValueNode(value)
        };

        public static implicit operator JsonNode(string value) => value == null ? null : new JsonValueNode(value);
        public static implicit operator JsonNode(int value) => new JsonValueNode(BrowserJson.Raw(value));
        public static implicit operator JsonNode(bool value) => new JsonValueNode(BrowserJson.Raw(value));
        public static implicit operator JsonNode(float value) => new JsonValueNode(BrowserJson.Raw(value));
        public static implicit operator JsonNode(double value) => new JsonValueNode(BrowserJson.Raw(value));
    }

    public sealed class JsonValueNode(object value) : JsonNode(value) { }

    public sealed class JsonObject : JsonNode
    {
        public JsonObject() : base(BrowserJson.NewObject()) { }
        internal JsonObject(object value) : base(value) { }

        public override JsonNode this[string name]
        {
            get => BrowserJson.HasProperty(Export(), name) ? Wrap(BrowserJson.Property(Export(), name)) : null;
            set => BrowserJson.SetProperty(Export(), name, value == null ? null : value.Export());
        }

        public bool Remove(string name) => BrowserJson.RemoveProperty(Export(), name);
    }

    public sealed class JsonArray : JsonNode, System.Collections.Generic.IEnumerable<JsonNode>
    {
        public JsonArray() : base(BrowserJson.NewArray()) { }
        public JsonArray(params JsonNode[] values) : this()
        {
            foreach (var value in values) Add(value);
        }
        internal JsonArray(object value) : base(value) { }

        public int Count => BrowserJson.ArrayLength(Export());
        public JsonNode this[int index] => Wrap(BrowserJson.ArrayItem(Export(), index));
        public void Add(JsonNode value) => BrowserJson.Add(Export(), value == null ? null : value.Export());

        public System.Collections.Generic.IEnumerator<JsonNode> GetEnumerator()
        {
            var result = new System.Collections.Generic.List<JsonNode>();
            foreach (var value in BrowserJson.Array(Export())) result.Add(Wrap(value));
            return result.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class BrowserJson
    {
        [Transpose.Template("Browser.BrowserJson.Parse({bytes})")]
        public static extern object Parse(byte[] bytes);

        [Transpose.Template("Browser.BrowserJson.SerializeBytes({value}, {indented}, {stringEnums})")]
        public static extern byte[] SerializeBytes(object value, bool indented, bool stringEnums);

        [Transpose.Template("Browser.BrowserJson.Serialize({value}, {indented}, {stringEnums})")]
        public static extern string Serialize(object value, bool indented, bool stringEnums);

        [Transpose.Template("Browser.BrowserJson.Clone({value})")]
        public static extern object Clone(object value);

        [Transpose.Template("Browser.BrowserJson.NewObject()")]
        public static extern object NewObject();

        [Transpose.Template("Browser.BrowserJson.NewArray()")]
        public static extern object NewArray();

        [Transpose.Template("Browser.BrowserJson.Raw({value})")]
        public static extern object Raw(int value);

        [Transpose.Template("Browser.BrowserJson.Raw({value})")]
        public static extern object Raw(bool value);

        [Transpose.Template("Browser.BrowserJson.Raw({value})")]
        public static extern object Raw(float value);

        [Transpose.Template("Browser.BrowserJson.Raw({value})")]
        public static extern object Raw(double value);

        [Transpose.Template("Browser.BrowserJson.Value({value})")]
        public static extern T Value<T>(object value);

        [Transpose.Template("Browser.BrowserJson.Kind({value})")]
        public static extern int Kind(object value);

        [Transpose.Template("Browser.BrowserJson.HasProperty({value}, {name})")]
        public static extern bool HasProperty(object value, string name);

        [Transpose.Template("Browser.BrowserJson.Property({value}, {name})")]
        public static extern object Property(object value, string name);

        [Transpose.Template("Browser.BrowserJson.SetProperty({target}, {name}, {value})")]
        public static extern void SetProperty(object target, string name, object value);

        [Transpose.Template("Browser.BrowserJson.RemoveProperty({target}, {name})")]
        public static extern bool RemoveProperty(object target, string name);

        [Transpose.Template("Browser.BrowserJson.Add({target}, {value})")]
        public static extern void Add(object target, object value);

        [Transpose.Template("Browser.BrowserJson.ArrayItem({value}, {index})")]
        public static extern object ArrayItem(object value, int index);

        [Transpose.Template("Browser.BrowserJson.ArrayLength({value})")]
        public static extern int ArrayLength(object value);

        [Transpose.Template("Browser.BrowserJson.Array({value})")]
        public static extern object[] Array(object value);

        [Transpose.Template("Browser.BrowserJson.Keys({value})")]
        public static extern string[] Keys(object value);

        [Transpose.Template("Browser.BrowserJson.Int32({value})")]
        public static extern int Int32(object value);

        [Transpose.Template("Browser.BrowserJson.Single({value})")]
        public static extern float Single(object value);

        [Transpose.Template("Browser.BrowserJson.String({value})")]
        public static extern string String(object value);

        [Transpose.Template("Browser.BrowserJson.Boolean({value})")]
        public static extern bool Boolean(object value);
    }

    public static class JsonNodes
    {
        public static JsonObject[] OfTypeObjects(JsonArray values)
        {
            if (values == null) return new JsonObject[0];
            var result = new System.Collections.Generic.List<JsonObject>();
            foreach (var value in values) if (value is JsonObject item) result.Add(item);
            return result.ToArray();
        }
    }

    public static class BrowserHash
    {
        [Transpose.Template("Browser.BrowserHash.Sha256({bytes})")]
        public static extern byte[] Sha256(System.ReadOnlySpan<byte> bytes);
    }

#if MOBY_INTERFACES
    public sealed class BrowserMobyModelInput(object value) : global::RatchetPs2.Core.Moby.IMobyModelInput
    {
        public bool FileExists(string relativePath) => BrowserMobyCallbacks.FileExists(value, relativePath);
        public bool DirectoryExists(string relativePath) => BrowserMobyCallbacks.DirectoryExists(value, relativePath);
        public byte[] ReadBytes(string relativePath) => BrowserMobyCallbacks.ReadBytes(value, relativePath);
        public System.Collections.Generic.IReadOnlyList<string> EnumerateDirectories(string relativePath) =>
            BrowserMobyCallbacks.EnumerateDirectories(value, relativePath);
        public System.Collections.Generic.IReadOnlyList<string> EnumerateFiles(string relativePath, string searchPattern = "*") =>
            BrowserMobyCallbacks.EnumerateFiles(value, relativePath, searchPattern);
    }

    public sealed class BrowserMobyModelOutput(object value) : global::RatchetPs2.Core.Moby.IMobyModelOutput
    {
        public void WriteBytes(string relativePath, System.ReadOnlySpan<byte> bytes) =>
            BrowserMobyCallbacks.WriteBytes(value, relativePath, bytes);
    }

    public static class BrowserMobyCallbacks
    {
        [Transpose.Template("Browser.BrowserMobyCallbacks.FileExists({value}, {path})")]
        public static extern bool FileExists(object value, string path);
        [Transpose.Template("Browser.BrowserMobyCallbacks.DirectoryExists({value}, {path})")]
        public static extern bool DirectoryExists(object value, string path);
        [Transpose.Template("Browser.BrowserMobyCallbacks.ReadBytes({value}, {path})")]
        public static extern byte[] ReadBytes(object value, string path);
        [Transpose.Template("Browser.BrowserMobyCallbacks.EnumerateDirectories({value}, {path})")]
        public static extern string[] EnumerateDirectories(object value, string path);
        [Transpose.Template("Browser.BrowserMobyCallbacks.EnumerateFiles({value}, {path}, {pattern})")]
        public static extern string[] EnumerateFiles(object value, string path, string pattern);
        [Transpose.Template("Browser.BrowserMobyCallbacks.WriteBytes({value}, {path}, {bytes})")]
        public static extern void WriteBytes(object value, string path, System.ReadOnlySpan<byte> bytes);
    }
#endif

    public sealed class InvalidDataException : System.SystemException
    {
        public InvalidDataException(string message) : base(message) { }
        public InvalidDataException(string message, System.Exception innerException) : base(message, innerException) { }
    }

    public sealed class BinaryReader : System.IDisposable
    {
        private readonly bool leaveOpen;
        public BinaryReader(System.IO.Stream input) : this(input, System.Text.Encoding.UTF8, false) { }
        public BinaryReader(System.IO.Stream input, System.Text.Encoding encoding, bool leaveOpen)
        {
            BaseStream = input ?? throw new System.ArgumentNullException(nameof(input));
            this.leaveOpen = leaveOpen;
        }

        public System.IO.Stream BaseStream { get; }
        public byte ReadByte()
        {
            var bytes = ReadBytesRequired(1);
            return bytes[0];
        }
        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());
        public short ReadInt16() => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(ReadBytesRequired(2));
        public ushort ReadUInt16() => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(ReadBytesRequired(2));
        public int ReadInt32() => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(ReadBytesRequired(4));
        public uint ReadUInt32() => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(ReadBytesRequired(4));
        public long ReadInt64() => unchecked((long)ReadUInt64());
        public ulong ReadUInt64()
        {
            var bytes = ReadBytesRequired(8);
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes) |
                ((ulong)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(System.ByteSpanExtensions.Slice((System.ReadOnlySpan<byte>)bytes, 4, 4)) << 32);
        }
        public float ReadSingle() => System.BitConverterCompatibility.ToSingle(ReadBytesRequired(4), 0);
        public byte[] ReadBytes(int count)
        {
            if (count < 0) throw new System.ArgumentOutOfRangeException(nameof(count));
            var bytes = new byte[count];
            var read = 0;
            while (read < count)
            {
                var current = BaseStream.Read(bytes, read, count - read);
                if (current == 0) break;
                read += current;
            }
            if (read == count) return bytes;
            var result = new byte[read];
            System.Array.Copy(bytes, result, read);
            return result;
        }
        private byte[] ReadBytesRequired(int count)
        {
            var bytes = ReadBytes(count);
            if (bytes.Length != count) throw new System.IO.EndOfStreamException();
            return bytes;
        }
        public void Dispose() { if (!leaveOpen) BaseStream.Dispose(); }
    }

    public sealed class BinaryWriter : System.IDisposable
    {
        private readonly bool leaveOpen;
        public BinaryWriter(System.IO.Stream output) : this(output, System.Text.Encoding.UTF8, false) { }
        public BinaryWriter(System.IO.Stream output, System.Text.Encoding encoding, bool leaveOpen)
        {
            BaseStream = output ?? throw new System.ArgumentNullException(nameof(output));
            this.leaveOpen = leaveOpen;
        }

        public System.IO.Stream BaseStream { get; }
        public void Flush() => BaseStream.Flush();
        public void Write(byte value) => BaseStream.WriteByte(value);
        public void Write(sbyte value) => Write(unchecked((byte)value));
        public void Write(short value) => Write(unchecked((ushort)value));
        public void Write(ushort value)
        {
            Write(unchecked((byte)value));
            Write(unchecked((byte)(value >> 8)));
        }
        public void Write(int value) => Write(unchecked((uint)value));
        public void Write(uint value)
        {
            Write(unchecked((byte)value));
            Write(unchecked((byte)(value >> 8)));
            Write(unchecked((byte)(value >> 16)));
            Write(unchecked((byte)(value >> 24)));
        }
        public void Write(float value) => Write(unchecked((uint)System.BitConverterCompatibility.SingleToInt32Bits(value)));
        public void Write(byte[] value) => BaseStream.Write(value, 0, value.Length);
        public void Write(System.ReadOnlySpan<byte> value) => Write(System.ByteSpanExtensions.CopyToArray(value));
        public void Write(System.Span<byte> value) => Write(System.ByteSpanExtensions.CopyToArray(value));
        public void Dispose() { Flush(); if (!leaveOpen) BaseStream.Dispose(); }
    }

    public enum CompressionMode { Decompress, Compress }
    public enum ZipArchiveMode { Read, Create, Update }

    public sealed class ZLibStream : System.IO.Stream
    {
        private readonly System.IO.Stream input;
        private readonly System.IO.MemoryStream data;

        public ZLibStream(System.IO.Stream input, CompressionMode mode)
        {
            if (mode != CompressionMode.Decompress) throw new System.NotSupportedException();
            this.input = input;
            using var compressed = new System.IO.MemoryStream();
            input.CopyTo(compressed);
            data = new System.IO.MemoryStream(BrowserCompression.Unzlib(compressed.ToArray()), false);
        }

        public override bool CanRead => data.CanRead;
        public override bool CanSeek => data.CanSeek;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => data.Position; set => data.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => data.Read(buffer, offset, count);
        public override long Seek(long offset, System.IO.SeekOrigin origin) => data.Seek(offset, origin);
        public override void SetLength(long value) => throw new System.NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { data.Dispose(); input.Dispose(); }
            base.Dispose(disposing);
        }
    }

    public sealed class ZipArchive : System.IDisposable
    {
        private readonly System.IO.Stream input;
        public ZipArchive(System.IO.Stream input, ZipArchiveMode mode)
        {
            if (mode != ZipArchiveMode.Read) throw new System.NotSupportedException();
            this.input = input;
            using var copy = new System.IO.MemoryStream();
            input.CopyTo(copy);
            var bytes = copy.ToArray();
            var entries = new System.Collections.Generic.List<ZipArchiveEntry>();
            foreach (var name in BrowserCompression.ZipNames(bytes))
                entries.Add(new ZipArchiveEntry(name, BrowserCompression.ZipEntry(bytes, name)));
            Entries = entries;
        }

        public System.Collections.Generic.IReadOnlyList<ZipArchiveEntry> Entries { get; }
        public void Dispose() => input.Dispose();
    }

    public sealed class ZipArchiveEntry(string fullName, byte[] data)
    {
        public string FullName { get; } = fullName;
        public long Length => BrowserCompression.Length(data);
        public System.IO.Stream Open() => new System.IO.MemoryStream(data, false);
    }

    public static class BrowserCompression
    {
        [Transpose.Template("Browser.BrowserCompression.Unzlib({bytes})")]
        public static extern byte[] Unzlib(byte[] bytes);

        [Transpose.Template("Browser.BrowserCompression.ZipNames({bytes})")]
        public static extern string[] ZipNames(byte[] bytes);

        [Transpose.Template("Browser.BrowserCompression.ZipEntry({bytes}, {name})")]
        public static extern byte[] ZipEntry(byte[] bytes, string name);

        [Transpose.Template("Browser.BrowserCompression.Length({bytes})")]
        public static extern long Length(byte[] bytes);
    }

    public static class NumericInputs
    {
        public static System.Numerics.Vector2[] Vector2Array(float[][] values)
        {
            var result = new System.Numerics.Vector2[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = new(values[i][0], values[i][1]);
            return result;
        }

        public static System.Numerics.Vector3[] Vector3Array(float[][] values)
        {
            var result = new System.Numerics.Vector3[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = new(values[i][0], values[i][1], values[i][2]);
            return result;
        }

        public static System.Numerics.Vector4[] Vector4Array(float[][] values)
        {
            var result = new System.Numerics.Vector4[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = new(values[i][0], values[i][1], values[i][2], values[i][3]);
            return result;
        }

        public static System.Numerics.Quaternion[] QuaternionArray(float[][] values)
        {
            var result = new System.Numerics.Quaternion[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = new(values[i][0], values[i][1], values[i][2], values[i][3]);
            return result;
        }

        public static System.Numerics.Matrix4x4[] Matrix4x4Array(float[][] values)
        {
            var result = new System.Numerics.Matrix4x4[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = new(
                values[i][0], values[i][1], values[i][2], values[i][3],
                values[i][4], values[i][5], values[i][6], values[i][7],
                values[i][8], values[i][9], values[i][10], values[i][11],
                values[i][12], values[i][13], values[i][14], values[i][15]);
            return result;
        }
    }
}

namespace System.IO
{
    public static class BrowserStreamExtensions
    {
        public static int Read(this Stream stream, Span<byte> buffer) => stream.Read(ByteSpanExtensions.Array(buffer), 0, buffer.Length);
        public static void Write(this Stream stream, byte[] buffer) => stream.Write(buffer, 0, buffer.Length);
        [Transpose.Template("Browser.BrowserStreamExtensions.Write({stream}, {buffer})")]
        public static extern void Write(this Stream stream, ReadOnlySpan<byte> buffer);
        public static void Write(this Stream stream, Span<byte> buffer) => Write(stream, ByteSpanExtensions.AsReadOnly(buffer));
        public static void ReadExactly(this Stream stream, byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var current = stream.Read(buffer, read, buffer.Length - read);
                if (current == 0) throw new EndOfStreamException();
                read += current;
            }
        }
    }
}

namespace System
{
    public static class BooleanCompatibility
    {
        public static bool And(bool left, bool right) => left && right;
        public static bool Or(bool left, bool right) => left || right;
        public static bool Xor(bool left, bool right) => left != right;
    }

    public static class PathCompatibility
    {
        public static string Combine(params string[] paths)
        {
            var result = string.Empty;
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                result = string.IsNullOrEmpty(result) || result.EndsWith("/") ? result + path.TrimStart('/') : result + "/" + path.TrimStart('/');
            }
            return result;
        }

        public static string GetFileName(string path)
        {
            var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return separator < 0 ? path : path.Substring(separator + 1);
        }

        public static string GetFileNameWithoutExtension(string path)
        {
            var name = GetFileName(path);
            var dot = name.LastIndexOf('.');
            return dot <= 0 ? name : name.Substring(0, dot);
        }

        public static string GetExtension(string path)
        {
            var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            var dot = path.LastIndexOf('.');
            return dot > separator ? path.Substring(dot) : string.Empty;
        }

        public static string ChangeExtension(string path, string extension)
        {
            var current = GetExtension(path);
            var stem = current.Length == 0 ? path : path.Substring(0, path.Length - current.Length);
            if (string.IsNullOrEmpty(extension)) return stem;
            return stem + (extension[0] == '.' ? extension : "." + extension);
        }
    }

    public static class BufferCompatibility
    {
        [Transpose.Template("Browser.BufferCompatibility.BlockCopy({source}, {sourceOffset}, {destination}, {destinationOffset}, {count})")]
        public static extern void BlockCopy(byte[] source, int sourceOffset, byte[] destination, int destinationOffset, int count);
    }

    public static class StringCompatibility
    {
        [Transpose.Template("Browser.StringCompatibility.Contains({value}, {part}, {comparison})")]
        public static extern bool Contains(string value, string part, StringComparison comparison);

        [Transpose.Template("Browser.StringCompatibility.Replace({value}, {oldValue}, {newValue}, {comparison})")]
        public static extern string Replace(string value, string oldValue, string newValue, StringComparison comparison);

        [Transpose.Template("Browser.StringCompatibility.IndexOf({value}, {part}, {comparison})")]
        public static extern int IndexOf(string value, char part, StringComparison comparison);
    }

    public static class CharCompatibility
    {
        [Transpose.Template("Browser.CharCompatibility.ToLowerInvariant({value})")]
        public static extern char ToLowerInvariant(char value);
    }

    public static class IntCompatibility
    {
        public static bool TryParse(string value, int start, out int result)
        {
            result = 0;
            if (value == null || start < 0 || start >= value.Length) return false;
            for (var i = start; i < value.Length; i++)
            {
                var digit = value[i] - '0';
                if (digit < 0 || digit > 9 || result > (int.MaxValue - digit) / 10) return false;
                result = result * 10 + digit;
            }
            return true;
        }
    }

    public sealed class ReadOnlyMemory<T>
    {
        private readonly T[] values;

        public ReadOnlyMemory(T[] values) => this.values = values;

        public ReadOnlySpan<T> Span => values;

        public T[] ToArray() => values;

        public static implicit operator ReadOnlyMemory<T>(T[] values) => new(values);
    }

    public static class ByteMemoryExtensions
    {
        public static byte[] Export(ReadOnlyMemory<byte> values) => values.ToArray();
    }

    public static class ByteSpanExtensions
    {
        private static readonly object views = CreateViews();

        [Transpose.Template("Browser.ByteSpanExtensions.CreateViews()")]
        private static extern object CreateViews();

        public static bool IsView(object value) => HasView(views, value);

        public static byte[] Export(byte[] bytes) => View(bytes, 0, bytes.Length);

        [Transpose.Template("Browser.ByteSpanExtensions.HasView({views}, {value})")]
        private static extern bool HasView(object views, object value);

        [Transpose.Template("Browser.ByteSpanExtensions.RegisterView({views}, {value})")]
        private static extern byte[] RegisterView(object views, byte[] value);

        public static Span<byte> AsSpan(byte[] array) => AsSpan(array, 0, array == null ? 0 : array.Length);
        public static Span<byte> AsSpan(byte[] array, int start) => AsSpan(array, start, (array == null ? 0 : array.Length) - start);
        public static Span<byte> AsSpan(byte[] array, int start, int length)
        {
            var size = array == null ? 0 : array.Length;
            Require(size, start, length);
            return Writable(View(array ?? new byte[0], start, length));
        }

        public static ReadOnlySpan<byte> AsReadOnly(byte[] array) => ReadOnly(Buffer(AsSpan(array)));
        public static Span<byte> Empty() => Writable(View(new byte[0], 0, 0));
        public static ReadOnlySpan<byte> EmptyReadOnly() => ReadOnly(View(new byte[0], 0, 0));

        public static ReadOnlySpan<byte> Slice(this ReadOnlySpan<byte> bytes, int start) => Slice(bytes, start, bytes.Length - start);
        public static ReadOnlySpan<byte> Slice(this ReadOnlySpan<byte> bytes, int start, int length)
        {
            Require(bytes.Length, start, length);
            return ReadOnly(View(Buffer(bytes), start, length));
        }

        public static Span<byte> Slice(Span<byte> bytes, int start) => Slice(bytes, start, bytes.Length - start);
        public static Span<byte> Slice(Span<byte> bytes, int start, int length)
        {
            Require(bytes.Length, start, length);
            return Writable(View(Buffer(bytes), start, length));
        }

        public static ReadOnlySpan<byte> Range(ReadOnlySpan<byte> bytes, int start, bool startFromEnd, int end, bool endFromEnd)
        {
            var offset = RangeOffset(bytes.Length, start, startFromEnd);
            return Slice(bytes, offset, RangeOffset(bytes.Length, end, endFromEnd) - offset);
        }

        public static Span<byte> Range(Span<byte> bytes, int start, bool startFromEnd, int end, bool endFromEnd)
        {
            var offset = RangeOffset(bytes.Length, start, startFromEnd);
            return Slice(bytes, offset, RangeOffset(bytes.Length, end, endFromEnd) - offset);
        }

        public static Span<byte> Range(byte[] bytes, int start, bool startFromEnd, int end, bool endFromEnd) =>
            Range(AsSpan(bytes), start, startFromEnd, end, endFromEnd);

        public static ReadOnlySpan<byte> Range(ReadOnlySpan<byte> bytes, Range range) =>
            Slice(bytes, range.Start.GetOffset(bytes.Length), range.End.GetOffset(bytes.Length) - range.Start.GetOffset(bytes.Length));

        public static Span<byte> Range(Span<byte> bytes, Range range) =>
            Slice(bytes, range.Start.GetOffset(bytes.Length), range.End.GetOffset(bytes.Length) - range.Start.GetOffset(bytes.Length));

        private static int RangeOffset(int size, int value, bool fromEnd)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            return fromEnd ? size - value : value;
        }

        private static void Require(int size, int start, int length)
        {
            if (start < 0 || length < 0 || start > size || length > size - start)
                throw new ArgumentOutOfRangeException(nameof(start));
        }

        public static byte Get(ReadOnlySpan<byte> bytes, int index, bool fromEnd) => GetArray(Buffer(bytes), index, fromEnd);
        public static byte Get(Span<byte> bytes, int index, bool fromEnd) => GetArray(Buffer(bytes), index, fromEnd);
        public static byte Get(ReadOnlySpan<byte> bytes, Index index) => Get(bytes, index.GetOffset(bytes.Length), false);
        public static byte Get(Span<byte> bytes, Index index) => Get(bytes, index.GetOffset(bytes.Length), false);
        [Transpose.Template("Browser.ByteSpanExtensions.GetArray({bytes}, {index}, {fromEnd})")]
        private static extern byte GetArray(byte[] bytes, int index, bool fromEnd);

        [Transpose.Template("Browser.ByteSpanExtensions.Set({bytes}, {index}, {fromEnd}, {value})")]
        public static extern byte Set(Span<byte> bytes, int index, bool fromEnd, byte value);

        public static byte Set(Span<byte> bytes, Index index, byte value) => Set(bytes, index.GetOffset(bytes.Length), false, value);

        public static byte Compound(Span<byte> bytes, int index, bool fromEnd, int value, int operation)
        {
            var current = Get(bytes, index, fromEnd);
            var result = operation switch
            {
                0 => unchecked((byte)(current + value)),
                1 => unchecked((byte)(current - value)),
                2 => unchecked((byte)(current * value)),
                3 => unchecked((byte)(current / value)),
                4 => unchecked((byte)(current % value)),
                5 => unchecked((byte)(current & value)),
                6 => unchecked((byte)(current | value)),
                7 => unchecked((byte)(current ^ value)),
                8 => unchecked((byte)(current << value)),
                _ => unchecked((byte)(current >> value))
            };
            return Set(bytes, index, fromEnd, result);
        }

        public static byte Compound(Span<byte> bytes, Index index, int value, int operation) =>
            Compound(bytes, index.GetOffset(bytes.Length), false, value, operation);

        private static int ElementOffset(int size, int index, bool fromEnd)
        {
            var offset = fromEnd ? size - index : index;
            if (offset < 0 || offset >= size) throw new IndexOutOfRangeException();
            return offset;
        }

        public static bool SequenceEqual(ReadOnlySpan<byte> span, ReadOnlySpan<byte> other)
        {
            if (span.Length != other.Length) return false;
            for (var i = 0; i < span.Length; i++) if (At(span, i) != At(other, i)) return false;
            return true;
        }

        public static bool SequenceEqual(Span<byte> span, ReadOnlySpan<byte> other) => SequenceEqual(ReadOnly(Buffer(span)), other);

        public static bool IsEmpty(ReadOnlySpan<byte> span) => span.Length == 0;
        public static bool IsEmpty(Span<byte> span) => span.Length == 0;

        public static int IndexOf(ReadOnlySpan<byte> span, byte value)
        {
            for (var i = 0; i < span.Length; i++) if (At(span, i) == value) return i;
            return -1;
        }

        public static int IndexOf(Span<byte> span, byte value) => IndexOf(ReadOnly(Buffer(span)), value);

        public static int IndexOf(ReadOnlySpan<byte> span, ReadOnlySpan<byte> value)
        {
            if (value.Length == 0) return 0;
            for (var i = 0; i <= span.Length - value.Length; i++)
            {
                var match = true;
                for (var j = 0; j < value.Length; j++) if (At(span, i + j) != At(value, j)) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }

        public static void ReadExactly(IO.Stream stream, Span<byte> buffer)
        {
            var bytes = Array(buffer);
            var read = 0;
            while (read < buffer.Length)
            {
                var current = stream.Read(bytes, read, buffer.Length - read);
                if (current == 0) throw new IO.EndOfStreamException();
                read += current;
            }
        }

        public static void CopyTo(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (source.Length > destination.Length) throw new ArgumentException("Destination is too short.");
            NativeCopy(source, destination);
        }

        public static void CopyTo(Span<byte> source, Span<byte> destination) => CopyTo(ReadOnly(Buffer(source)), destination);
        public static void CopyTo(byte[] source, Span<byte> destination) => CopyTo(ReadOnly(source), destination);

        public static bool TryCopyTo(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (source.Length > destination.Length) return false;
            CopyTo(source, destination);
            return true;
        }

        public static bool TryCopyTo(Span<byte> source, Span<byte> destination) => TryCopyTo(ReadOnly(Buffer(source)), destination);
        public static bool TryCopyTo(byte[] source, Span<byte> destination) => TryCopyTo(ReadOnly(source), destination);

        public static void Clear(Span<byte> bytes) => Fill(bytes, 0);

        public static void Fill(Span<byte> bytes, byte value) => NativeFill(bytes, value);

        [Transpose.Template("Browser.ByteSpanExtensions.NativeCopy({source}, {destination})")]
        private static extern void NativeCopy(ReadOnlySpan<byte> source, Span<byte> destination);

        [Transpose.Template("Browser.ByteSpanExtensions.NativeFill({bytes}, {value})")]
        private static extern void NativeFill(Span<byte> bytes, byte value);

        private static byte[] View(byte[] bytes, int start, int length)
        {
            return RegisterView(views, CreateView(bytes, start, length));
        }

        // Transpose also creates ordinary arrays internally. Their span views must
        // alias the original array; converting them to Uint8Array would silently copy.
        // Ordinary-array spans need proxies to preserve aliasing. Byte arrays created
        // by the SDK runtime are Uint8Array instances and stay native views.
        [Transpose.Template("Browser.ByteSpanExtensions.CreateView({bytes}, {start}, {length})")]
        private static extern byte[] CreateView(byte[] bytes, int start, int length);

        [Transpose.Template("Browser.ByteSpanExtensions.Writable({bytes})")]
        private static extern Span<byte> Writable(byte[] bytes);

        [Transpose.Template("Browser.ByteSpanExtensions.ReadOnly({bytes})")]
        private static extern ReadOnlySpan<byte> ReadOnly(byte[] bytes);

        [Transpose.Template("Browser.ByteSpanExtensions.AsReadOnly({bytes})")]
        public static extern ReadOnlySpan<byte> AsReadOnly(Span<byte> bytes);

        [Transpose.Template("Browser.ByteSpanExtensions.Buffer({bytes})")]
        private static extern byte[] Buffer(ReadOnlySpan<byte> bytes);

        [Transpose.Template("Browser.ByteSpanExtensions.Buffer({bytes})")]
        private static extern byte[] Buffer(Span<byte> bytes);

        [Transpose.Template("Browser.ByteSpanExtensions.Array({bytes})")]
        public static extern byte[] Array(Span<byte> bytes);

        [Transpose.Template("Browser.ByteSpanExtensions.At({bytes}, {index})")]
        private static extern byte At(ReadOnlySpan<byte> bytes, int index);

        [Transpose.Template("Browser.ByteSpanExtensions.CopyToArray({bytes})")]
        public static extern byte[] CopyToArray(this ReadOnlySpan<byte> bytes);

        [Transpose.Template("Browser.ByteSpanExtensions.CopyToArray({bytes})")]
        public static extern byte[] CopyToArray(Span<byte> bytes);
    }

    public static class IntSpanExtensions
    {
        [Transpose.Template("Browser.IntSpanExtensions.Set({values}, {index}, {value})")]
        public static extern int Set(Span<int> values, int index, int value);

        public static void Clear(Span<int> values)
        {
            var buffer = Buffer(values);
            for (var i = 0; i < values.Length; i++) buffer[i] = 0;
        }

        [Transpose.Template("Browser.IntSpanExtensions.Buffer({values})")]
        private static extern int[] Buffer(Span<int> values);
    }

    public static class ArrayCompatibility
    {
        [Transpose.Template("Browser.ArrayCompatibility.NewUninitialized({length})")]
        public static extern T[] NewUninitialized<T>(int length);

        public static void Fill<T>(T[] array, T value)
        {
            for (var i = 0; i < array.Length; i++) array[i] = value;
        }

        [Transpose.Template("Browser.ArrayCompatibility.Sort({array})")]
        public static extern void Sort(uint[] array);
    }

    public static class BitConverterCompatibility
    {
        [Transpose.Template("Browser.BitConverterCompatibility.Int32BitsToSingle({value})")]
        public static extern float Int32BitsToSingle(int value);

        [Transpose.Template("Browser.BitConverterCompatibility.SingleToInt32Bits({value})")]
        public static extern int SingleToInt32Bits(float value);

        [Transpose.Template("Browser.BitConverterCompatibility.ToInt16({value}, {startIndex})")]
        public static extern short ToInt16(byte[] value, int startIndex);

        [Transpose.Template("Browser.BitConverterCompatibility.ToUInt16({value}, {startIndex})")]
        public static extern ushort ToUInt16(byte[] value, int startIndex);

        [Transpose.Template("Browser.BitConverterCompatibility.ToInt32({value}, {startIndex})")]
        public static extern int ToInt32(byte[] value, int startIndex);

        [Transpose.Template("Browser.BitConverterCompatibility.ToUInt32({value}, {startIndex})")]
        public static extern uint ToUInt32(byte[] value, int startIndex);

        [Transpose.Template("Browser.BitConverterCompatibility.ToSingle({value}, {startIndex})")]
        public static extern float ToSingle(byte[] value, int startIndex);

        public static ushort ToUInt16(ReadOnlySpan<byte> value) =>
            Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(value);

        public static uint ToUInt32(ReadOnlySpan<byte> value) =>
            Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(value);

        public static byte[] GetBytes(short value) => GetBytes(unchecked((ushort)value));

        public static byte[] GetBytes(ushort value)
        {
            var bytes = RatchetPs2.JavaScript.ByteArrays.New(2);
            Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            return bytes;
        }

        public static byte[] GetBytes(int value) => GetBytes(unchecked((uint)value));

        public static byte[] GetBytes(uint value)
        {
            var bytes = RatchetPs2.JavaScript.ByteArrays.New(4);
            Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            return bytes;
        }

        public static byte[] GetBytes(float value)
        {
            var bytes = RatchetPs2.JavaScript.ByteArrays.New(4);
            Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
            return bytes;
        }

    }

    public static class CheckedArithmetic
    {
        [Transpose.Template("Browser.CheckedArithmetic.AddInt32({left}, {right})")]
        public static extern int AddInt32(int left, int right);

        [Transpose.Template("Browser.CheckedArithmetic.SubtractInt32({left}, {right})")]
        public static extern int SubtractInt32(int left, int right);

        [Transpose.Template("Browser.CheckedArithmetic.MultiplyInt32({left}, {right})")]
        public static extern int MultiplyInt32(int left, int right);

        public static int DivideInt32(int left, int right)
        {
            if (left == int.MinValue && right == -1) throw new OverflowException();
            return left / right;
        }

        [Transpose.Template("Browser.CheckedArithmetic.AddUInt32({left}, {right})")]
        public static extern uint AddUInt32(uint left, uint right);

        public static uint SubtractUInt32(uint left, uint right)
        {
            if (left < right) throw new OverflowException();
            return left - right;
        }

        [Transpose.Template("Browser.CheckedArithmetic.MultiplyUInt32({left}, {right})")]
        public static extern uint MultiplyUInt32(uint left, uint right);

        [Transpose.Template("Browser.CheckedArithmetic.AddInt64({left}, {right})")]
        public static extern long AddInt64(long left, long right);

        [Transpose.Template("Browser.CheckedArithmetic.SubtractInt64({left}, {right})")]
        public static extern long SubtractInt64(long left, long right);

        public static long MultiplyInt64(long left, long right)
        {
            if (left > 0 && (right > 0 && left > long.MaxValue / right || right < 0 && right < long.MinValue / left) ||
                left < 0 && (right > 0 && left < long.MinValue / right || right < 0 && left < long.MaxValue / right))
                throw new OverflowException();
            return left * right;
        }

        [Transpose.Template("Browser.CheckedArithmetic.AddUInt64({left}, {right})")]
        public static extern ulong AddUInt64(ulong left, ulong right);

        [Transpose.Template("Browser.CheckedArithmetic.SubtractUInt64({left}, {right})")]
        public static extern ulong SubtractUInt64(ulong left, ulong right);

        public static ulong MultiplyUInt64(ulong left, ulong right)
        {
            if (right != 0 && left > ulong.MaxValue / right) throw new OverflowException();
            return left * right;
        }

        public static byte ToByte(long value)
        {
            if (value < byte.MinValue || value > byte.MaxValue) throw new OverflowException();
            return unchecked((byte)value);
        }

        public static sbyte ToSByte(long value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) throw new OverflowException();
            return unchecked((sbyte)value);
        }

        public static short ToInt16(long value)
        {
            if (value < short.MinValue || value > short.MaxValue) throw new OverflowException();
            return unchecked((short)value);
        }

        public static ushort ToUInt16(long value)
        {
            if (value < ushort.MinValue || value > ushort.MaxValue) throw new OverflowException();
            return unchecked((ushort)value);
        }

        public static int ToInt32(long value)
        {
            if (value < int.MinValue || value > int.MaxValue) throw new OverflowException();
            return unchecked((int)value);
        }

        [Transpose.Template("Browser.CheckedArithmetic.ToInt32({value})")]
        public static extern int ToInt32(uint value);

        public static uint ToUInt32(long value)
        {
            if (value < uint.MinValue || value > uint.MaxValue) throw new OverflowException();
            return unchecked((uint)value);
        }

        public static uint ToUInt32(ulong value)
        {
            if (value > uint.MaxValue) throw new OverflowException();
            return unchecked((uint)value);
        }

        [Transpose.Template("Browser.CheckedArithmetic.ToInt16({value})")]
        public static extern short ToInt16(float value);

        [Transpose.Template("Browser.CheckedArithmetic.SingleToInt32({value})")]
        public static extern int ToInt32(float value);
    }

    public static class FloatMath
    {
        [Transpose.Template("Browser.FloatMath.Round({value})")]
        public static extern float Round(float value);

        [Transpose.Template("Browser.FloatMath.Add({left}, {right})")]
        public static extern float Add(float left, float right);

        [Transpose.Template("Browser.FloatMath.Subtract({left}, {right})")]
        public static extern float Subtract(float left, float right);

        [Transpose.Template("Browser.FloatMath.Multiply({left}, {right})")]
        public static extern float Multiply(float left, float right);

        [Transpose.Template("Browser.FloatMath.Divide({left}, {right})")]
        public static extern float Divide(float left, float right);

        [Transpose.Template("Browser.FloatMath.Remainder({left}, {right})")]
        public static extern float Remainder(float left, float right);
    }
}

namespace System.Collections.Generic
{
    public static class ReadOnlyListCompatibility
    {
        [Transpose.Template("Browser.ReadOnlyListCompatibility.Value({value})")]
        public static extern T Value<T>(T value);
    }

    public sealed class SortedDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        private readonly Dictionary<TKey, TValue> values = new();
        private readonly List<TKey> keys = new();

        public int Count => values.Count;
        public TKey[] Keys => keys.ToArray();
        public TValue this[TKey key]
        {
            get => values[key];
            set
            {
                if (values.TryGetValue(key, out _)) values[key] = value;
                else Add(key, value);
            }
        }
        public void Add(TKey key, TValue value)
        {
            values.Add(key, value);
            var index = 0;
            while (index < keys.Count && Compare(keys[index], key) < 0) index++;
            keys.Insert(index, key);
        }
        public bool TryGetValue(TKey key, out TValue value) => values.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            var result = new List<KeyValuePair<TKey, TValue>>();
            foreach (var key in keys) result.Add(new KeyValuePair<TKey, TValue>(key, values[key]));
            return result.GetEnumerator();
        }
        Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        [Transpose.Template("Browser.SortedDictionary.Compare({left}, {right})")]
        private static extern int Compare(TKey left, TKey right);
    }

    public static class DictionaryCompatibility
    {
        public static Dictionary<TKey, TValue> Copy<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source)
        {
            var result = new Dictionary<TKey, TValue>();
            foreach (var pair in source) result.Add(pair.Key, pair.Value);
            return result;
        }
    }

    public static class QueueCompatibility
    {
        public static bool TryDequeue<T>(Queue<T> queue, out T value)
        {
            if (queue.Count == 0) { value = default; return false; }
            value = queue.Dequeue();
            return true;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    public sealed class ConditionalWeakTable<TKey, TValue> where TKey : class where TValue : class
    {
        private readonly System.Collections.Generic.Dictionary<TKey, TValue> values = new();
        public void Add(TKey key, TValue value) => values.Add(key, value);
        public bool TryGetValue(TKey key, out TValue value) => values.TryGetValue(key, out value);
    }
}

namespace System.Diagnostics
{
    public static class StopwatchCompatibility
    {
        public static TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency);
    }
}

namespace System.Numerics
{
    public struct Vector2
    {
        public float X;
        public float Y;

        public Vector2(float value) : this(value, value) { }
        public Vector2(float x, float y) { X = FloatMath.Round(x); Y = FloatMath.Round(y); }

        public static Vector2 Zero => new(0f);
        public static Vector2 One => new(1f);

        public float LengthSquared() => X * X + Y * Y;

        public static Vector2 Clamp(Vector2 value, Vector2 min, Vector2 max) => Min(Max(value, min), max);
        public static float DistanceSquared(Vector2 left, Vector2 right) => (left - right).LengthSquared();
        public static Vector2 Min(Vector2 left, Vector2 right) => new(MathF.Min(left.X, right.X), MathF.Min(left.Y, right.Y));
        public static Vector2 Max(Vector2 left, Vector2 right) => new(MathF.Max(left.X, right.X), MathF.Max(left.Y, right.Y));

        public static Vector2 operator +(Vector2 left, Vector2 right) => new(left.X + right.X, left.Y + right.Y);
        public static Vector2 operator -(Vector2 left, Vector2 right) => new(left.X - right.X, left.Y - right.Y);
        public static Vector2 operator *(Vector2 left, Vector2 right) => new(left.X * right.X, left.Y * right.Y);
        public static Vector2 operator *(Vector2 value, float scale) => new(value.X * scale, value.Y * scale);
        public static Vector2 operator /(Vector2 value, float scale) => new(value.X / scale, value.Y / scale);
    }

    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3(float value) : this(value, value, value) { }
        public Vector3(float x, float y, float z) { X = FloatMath.Round(x); Y = FloatMath.Round(y); Z = FloatMath.Round(z); }

        public static Vector3 Zero => new(0f);
        public static Vector3 One => new(1f);
        public static Vector3 UnitY => new(0f, 1f, 0f);
        public static Vector3 UnitZ => new(0f, 0f, 1f);

        public float Length() => MathF.Sqrt(LengthSquared());
        public float LengthSquared() => X * X + Y * Y + Z * Z;

        public static Vector3 Cross(Vector3 left, Vector3 right) => new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
        public static float Distance(Vector3 left, Vector3 right) => (left - right).Length();
        public static float DistanceSquared(Vector3 left, Vector3 right) => (left - right).LengthSquared();
        public static float Dot(Vector3 left, Vector3 right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        public static Vector3 Min(Vector3 left, Vector3 right) => new(
            MathF.Min(left.X, right.X), MathF.Min(left.Y, right.Y), MathF.Min(left.Z, right.Z));
        public static Vector3 Max(Vector3 left, Vector3 right) => new(
            MathF.Max(left.X, right.X), MathF.Max(left.Y, right.Y), MathF.Max(left.Z, right.Z));
        public static Vector3 Normalize(Vector3 value) => value / value.Length();
        public static Vector3 Transform(Vector3 position, Matrix4x4 matrix) => new(
            position.X * matrix.M11 + position.Y * matrix.M21 + position.Z * matrix.M31 + matrix.M41,
            position.X * matrix.M12 + position.Y * matrix.M22 + position.Z * matrix.M32 + matrix.M42,
            position.X * matrix.M13 + position.Y * matrix.M23 + position.Z * matrix.M33 + matrix.M43);
        public static Vector3 Transform(Vector3 value, Quaternion rotation)
        {
            var x2 = rotation.X + rotation.X;
            var y2 = rotation.Y + rotation.Y;
            var z2 = rotation.Z + rotation.Z;
            var wx2 = rotation.W * x2;
            var wy2 = rotation.W * y2;
            var wz2 = rotation.W * z2;
            var xx2 = rotation.X * x2;
            var xy2 = rotation.X * y2;
            var xz2 = rotation.X * z2;
            var yy2 = rotation.Y * y2;
            var yz2 = rotation.Y * z2;
            var zz2 = rotation.Z * z2;
            return new Vector3(
                value.X * (1f - yy2 - zz2) + value.Y * (xy2 - wz2) + value.Z * (xz2 + wy2),
                value.X * (xy2 + wz2) + value.Y * (1f - xx2 - zz2) + value.Z * (yz2 - wx2),
                value.X * (xz2 - wy2) + value.Y * (yz2 + wx2) + value.Z * (1f - xx2 - yy2));
        }

        public static Vector3 operator +(Vector3 left, Vector3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static Vector3 operator -(Vector3 left, Vector3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static Vector3 operator -(Vector3 value) => new(-value.X, -value.Y, -value.Z);
        public static Vector3 operator *(Vector3 value, float scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
        public static Vector3 operator /(Vector3 value, float scale) => new(value.X / scale, value.Y / scale, value.Z / scale);
        public static bool operator ==(Vector3 left, Vector3 right) => left.X == right.X && left.Y == right.Y && left.Z == right.Z;
        public static bool operator !=(Vector3 left, Vector3 right) => !(left == right);
    }

    public struct Vector4
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Vector4(float x, float y, float z, float w)
        {
            X = FloatMath.Round(x); Y = FloatMath.Round(y); Z = FloatMath.Round(z); W = FloatMath.Round(w);
        }

        public static Vector4 Zero => new(0f, 0f, 0f, 0f);
        public static Vector4 One => new(1f, 1f, 1f, 1f);

        public static Vector4 operator /(Vector4 value, float scale) =>
            new(value.X / scale, value.Y / scale, value.Z / scale, value.W / scale);
    }

    public struct Quaternion
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Quaternion(float x, float y, float z, float w)
        {
            X = FloatMath.Round(x); Y = FloatMath.Round(y); Z = FloatMath.Round(z); W = FloatMath.Round(w);
        }

        public static Quaternion Identity => new(0f, 0f, 0f, 1f);

        public float LengthSquared() => X * X + Y * Y + Z * Z + W * W;

        public static Quaternion CreateFromRotationMatrix(Matrix4x4 matrix)
        {
            var trace = matrix.M11 + matrix.M22 + matrix.M33;
            if (trace > 0f)
            {
                var s = MathF.Sqrt(trace + 1f);
                var inverse = 0.5f / s;
                return new Quaternion(
                    (matrix.M23 - matrix.M32) * inverse,
                    (matrix.M31 - matrix.M13) * inverse,
                    (matrix.M12 - matrix.M21) * inverse,
                    s * 0.5f);
            }
            if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
            {
                var s = MathF.Sqrt(1f + matrix.M11 - matrix.M22 - matrix.M33);
                var inverse = 0.5f / s;
                return new Quaternion(s * 0.5f, (matrix.M12 + matrix.M21) * inverse,
                    (matrix.M13 + matrix.M31) * inverse, (matrix.M23 - matrix.M32) * inverse);
            }
            if (matrix.M22 > matrix.M33)
            {
                var s = MathF.Sqrt(1f + matrix.M22 - matrix.M11 - matrix.M33);
                var inverse = 0.5f / s;
                return new Quaternion((matrix.M21 + matrix.M12) * inverse, s * 0.5f,
                    (matrix.M32 + matrix.M23) * inverse, (matrix.M31 - matrix.M13) * inverse);
            }
            else
            {
                var s = MathF.Sqrt(1f + matrix.M33 - matrix.M11 - matrix.M22);
                var inverse = 0.5f / s;
                return new Quaternion((matrix.M31 + matrix.M13) * inverse, (matrix.M32 + matrix.M23) * inverse,
                    s * 0.5f, (matrix.M12 - matrix.M21) * inverse);
            }
        }

        public static float Dot(Quaternion left, Quaternion right) =>
            left.X * right.X + left.Y * right.Y + left.Z * right.Z + left.W * right.W;
        public static Quaternion Inverse(Quaternion value)
        {
            if (value.LengthSquared() == 0f) return new Quaternion();
            var inverse = 1f / value.LengthSquared();
            return new Quaternion(-value.X * inverse, -value.Y * inverse, -value.Z * inverse, value.W * inverse);
        }
        public static Quaternion Normalize(Quaternion value)
        {
            var inverse = 1f / MathF.Sqrt(value.LengthSquared());
            return new Quaternion(value.X * inverse, value.Y * inverse, value.Z * inverse, value.W * inverse);
        }

        public static Quaternion operator *(Quaternion left, Quaternion right) => new(
            left.X * right.W + right.X * left.W + left.Y * right.Z - left.Z * right.Y,
            left.Y * right.W + right.Y * left.W + left.Z * right.X - left.X * right.Z,
            left.Z * right.W + right.Z * left.W + left.X * right.Y - left.Y * right.X,
            left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);
        public static bool operator ==(Quaternion left, Quaternion right) =>
            left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;
        public static bool operator !=(Quaternion left, Quaternion right) => !(left == right);
    }

    public struct Matrix4x4
    {
        private const float InvertEpsilon = 2.938737E-39f;

        public float M11; public float M12; public float M13; public float M14;
        public float M21; public float M22; public float M23; public float M24;
        public float M31; public float M32; public float M33; public float M34;
        public float M41; public float M42; public float M43; public float M44;

        public Matrix4x4(
            float m11, float m12, float m13, float m14,
            float m21, float m22, float m23, float m24,
            float m31, float m32, float m33, float m34,
            float m41, float m42, float m43, float m44)
        {
            M11 = FloatMath.Round(m11); M12 = FloatMath.Round(m12); M13 = FloatMath.Round(m13); M14 = FloatMath.Round(m14);
            M21 = FloatMath.Round(m21); M22 = FloatMath.Round(m22); M23 = FloatMath.Round(m23); M24 = FloatMath.Round(m24);
            M31 = FloatMath.Round(m31); M32 = FloatMath.Round(m32); M33 = FloatMath.Round(m33); M34 = FloatMath.Round(m34);
            M41 = FloatMath.Round(m41); M42 = FloatMath.Round(m42); M43 = FloatMath.Round(m43); M44 = FloatMath.Round(m44);
        }

        public static Matrix4x4 Identity => new(
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f);

        public static Matrix4x4 CreateFromQuaternion(Quaternion value)
        {
            var xx = value.X * value.X;
            var yy = value.Y * value.Y;
            var zz = value.Z * value.Z;
            var xy = value.X * value.Y;
            var wz = value.Z * value.W;
            var xz = value.Z * value.X;
            var wy = value.Y * value.W;
            var yz = value.Y * value.Z;
            var wx = value.X * value.W;
            return new Matrix4x4(
                1f - 2f * (yy + zz), 2f * (xy + wz), 2f * (xz - wy), 0f,
                2f * (xy - wz), 1f - 2f * (zz + xx), 2f * (yz + wx), 0f,
                2f * (xz + wy), 2f * (yz - wx), 1f - 2f * (yy + xx), 0f,
                0f, 0f, 0f, 1f);
        }

        public static Matrix4x4 CreateRotationX(float radians)
        {
            var cosine = MathF.Cos(radians);
            var sine = MathF.Sin(radians);
            return new Matrix4x4(
                1f, 0f, 0f, 0f,
                0f, cosine, sine, 0f,
                0f, -sine, cosine, 0f,
                0f, 0f, 0f, 1f);
        }

        public static Matrix4x4 CreateRotationY(float radians)
        {
            var cosine = MathF.Cos(radians);
            var sine = MathF.Sin(radians);
            return new Matrix4x4(
                cosine, 0f, -sine, 0f,
                0f, 1f, 0f, 0f,
                sine, 0f, cosine, 0f,
                0f, 0f, 0f, 1f);
        }

        public static Matrix4x4 CreateScale(Vector3 value) => new(
            value.X, 0f, 0f, 0f,
            0f, value.Y, 0f, 0f,
            0f, 0f, value.Z, 0f,
            0f, 0f, 0f, 1f);

        public static Matrix4x4 CreateTranslation(Vector3 value) => new(
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            value.X, value.Y, value.Z, 1f);

        public static Matrix4x4 Transpose(Matrix4x4 value) => new(
            value.M11, value.M21, value.M31, value.M41,
            value.M12, value.M22, value.M32, value.M42,
            value.M13, value.M23, value.M33, value.M43,
            value.M14, value.M24, value.M34, value.M44);

        public static bool Invert(Matrix4x4 value, out Matrix4x4 result)
        {
            var a = value.M11; var b = value.M12; var c = value.M13; var d = value.M14;
            var e = value.M21; var f = value.M22; var g = value.M23; var h = value.M24;
            var i = value.M31; var j = value.M32; var k = value.M33; var l = value.M34;
            var m = value.M41; var n = value.M42; var o = value.M43; var p = value.M44;
            var kp_lo = k * p - l * o;
            var jp_ln = j * p - l * n;
            var jo_kn = j * o - k * n;
            var ip_lm = i * p - l * m;
            var io_km = i * o - k * m;
            var in_jm = i * n - j * m;
            var a11 = f * kp_lo - g * jp_ln + h * jo_kn;
            var a12 = -(e * kp_lo - g * ip_lm + h * io_km);
            var a13 = e * jp_ln - f * ip_lm + h * in_jm;
            var a14 = -(e * jo_kn - f * io_km + g * in_jm);
            var determinant = a * a11 + b * a12 + c * a13 + d * a14;
            if (MathF.Abs(determinant) < InvertEpsilon)
            {
                result = new Matrix4x4(
                    float.NaN, float.NaN, float.NaN, float.NaN,
                    float.NaN, float.NaN, float.NaN, float.NaN,
                    float.NaN, float.NaN, float.NaN, float.NaN,
                    float.NaN, float.NaN, float.NaN, float.NaN);
                return false;
            }
            var inverse = 1f / determinant;
            var gp_ho = g * p - h * o;
            var fp_hn = f * p - h * n;
            var fo_gn = f * o - g * n;
            var ep_hm = e * p - h * m;
            var eo_gm = e * o - g * m;
            var en_fm = e * n - f * m;
            var gl_hk = g * l - h * k;
            var fl_hj = f * l - h * j;
            var fk_gj = f * k - g * j;
            var el_hi = e * l - h * i;
            var ek_gi = e * k - g * i;
            var ej_fi = e * j - f * i;
            result = new Matrix4x4(
                a11 * inverse,
                -(b * kp_lo - c * jp_ln + d * jo_kn) * inverse,
                (b * gp_ho - c * fp_hn + d * fo_gn) * inverse,
                -(b * gl_hk - c * fl_hj + d * fk_gj) * inverse,
                a12 * inverse,
                (a * kp_lo - c * ip_lm + d * io_km) * inverse,
                -(a * gp_ho - c * ep_hm + d * eo_gm) * inverse,
                (a * gl_hk - c * el_hi + d * ek_gi) * inverse,
                a13 * inverse,
                -(a * jp_ln - b * ip_lm + d * in_jm) * inverse,
                (a * fp_hn - b * ep_hm + d * en_fm) * inverse,
                -(a * fl_hj - b * el_hi + d * ej_fi) * inverse,
                a14 * inverse,
                (a * jo_kn - b * io_km + c * in_jm) * inverse,
                -(a * fo_gn - b * eo_gm + c * en_fm) * inverse,
                (a * fk_gj - b * ek_gi + c * ej_fi) * inverse);
            return true;
        }

        public static Matrix4x4 operator *(Matrix4x4 left, Matrix4x4 right) => new(
            left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31 + left.M14 * right.M41,
            left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32 + left.M14 * right.M42,
            left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33 + left.M14 * right.M43,
            left.M11 * right.M14 + left.M12 * right.M24 + left.M13 * right.M34 + left.M14 * right.M44,
            left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31 + left.M24 * right.M41,
            left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32 + left.M24 * right.M42,
            left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33 + left.M24 * right.M43,
            left.M21 * right.M14 + left.M22 * right.M24 + left.M23 * right.M34 + left.M24 * right.M44,
            left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31 + left.M34 * right.M41,
            left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32 + left.M34 * right.M42,
            left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33 + left.M34 * right.M43,
            left.M31 * right.M14 + left.M32 * right.M24 + left.M33 * right.M34 + left.M34 * right.M44,
            left.M41 * right.M11 + left.M42 * right.M21 + left.M43 * right.M31 + left.M44 * right.M41,
            left.M41 * right.M12 + left.M42 * right.M22 + left.M43 * right.M32 + left.M44 * right.M42,
            left.M41 * right.M13 + left.M42 * right.M23 + left.M43 * right.M33 + left.M44 * right.M43,
            left.M41 * right.M14 + left.M42 * right.M24 + left.M43 * right.M34 + left.M44 * right.M44);

        public static bool operator ==(Matrix4x4 left, Matrix4x4 right) =>
            left.M11 == right.M11 && left.M12 == right.M12 && left.M13 == right.M13 && left.M14 == right.M14 &&
            left.M21 == right.M21 && left.M22 == right.M22 && left.M23 == right.M23 && left.M24 == right.M24 &&
            left.M31 == right.M31 && left.M32 == right.M32 && left.M33 == right.M33 && left.M34 == right.M34 &&
            left.M41 == right.M41 && left.M42 == right.M42 && left.M43 == right.M43 && left.M44 == right.M44;
        public static bool operator !=(Matrix4x4 left, Matrix4x4 right) => !(left == right);
    }

    public static class BitOperations
    {
        public static int Log2(uint value)
        {
            var result = 0;
            if (value >= 1u << 16) { value >>= 16; result += 16; }
            if (value >= 1u << 8) { value >>= 8; result += 8; }
            if (value >= 1u << 4) { value >>= 4; result += 4; }
            if (value >= 1u << 2) { value >>= 2; result += 2; }
            if (value >= 1u << 1) result++;
            return result;
        }
    }
}

namespace System.Buffers.Binary
{
    public static class BinaryPrimitives
    {
        [Transpose.Template("Browser.BinaryPrimitives.ReadUInt16LittleEndian({bytes})")]
        public static extern ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> bytes);

        public static short ReadInt16LittleEndian(ReadOnlySpan<byte> bytes) =>
            unchecked((short)ReadUInt16LittleEndian(bytes));

        [Transpose.Template("Browser.BinaryPrimitives.ReadUInt16BigEndian({bytes})")]
        public static extern ushort ReadUInt16BigEndian(ReadOnlySpan<byte> bytes);

        [Transpose.Template("Browser.BinaryPrimitives.ReadUInt32LittleEndian({bytes})")]
        public static extern uint ReadUInt32LittleEndian(ReadOnlySpan<byte> bytes);

        public static int ReadInt32LittleEndian(ReadOnlySpan<byte> bytes) =>
            unchecked((int)ReadUInt32LittleEndian(bytes));

        [Transpose.Template("Browser.BinaryPrimitives.ReadUInt32BigEndian({bytes})")]
        public static extern uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes);

        public static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes) => unchecked((int)ReadUInt32BigEndian(bytes));

        public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> bytes)
        {
            Require(bytes, 8);
            return ReadUInt32LittleEndian(bytes) | ((ulong)ReadUInt32LittleEndian(bytes.Slice(4, 4)) << 32);
        }

        public static float ReadSingleLittleEndian(ReadOnlySpan<byte> bytes) =>
            BitConverterCompatibility.Int32BitsToSingle(ReadInt32LittleEndian(bytes));

        public static void WriteUInt16LittleEndian(Span<byte> destination, ushort value)
        {
            Require(destination, 2);
            Put(destination, 0, (byte)value);
            Put(destination, 1, (byte)(value >> 8));
        }

        public static void WriteInt16LittleEndian(Span<byte> destination, short value) =>
            WriteUInt16LittleEndian(destination, unchecked((ushort)value));

        public static void WriteUInt32LittleEndian(Span<byte> destination, uint value)
        {
            Require(destination, 4);
            Put(destination, 0, (byte)value);
            Put(destination, 1, (byte)(value >> 8));
            Put(destination, 2, (byte)(value >> 16));
            Put(destination, 3, (byte)(value >> 24));
        }

        public static void WriteInt32LittleEndian(Span<byte> destination, int value) =>
            WriteUInt32LittleEndian(destination, unchecked((uint)value));

        public static void WriteUInt32BigEndian(Span<byte> destination, uint value)
        {
            Require(destination, 4);
            Put(destination, 0, (byte)(value >> 24));
            Put(destination, 1, (byte)(value >> 16));
            Put(destination, 2, (byte)(value >> 8));
            Put(destination, 3, (byte)value);
        }

        public static void WriteSingleLittleEndian(Span<byte> destination, float value) =>
            WriteInt32LittleEndian(destination, BitConverterCompatibility.SingleToInt32Bits(value));

        private static void Require(ReadOnlySpan<byte> bytes, int length)
        {
            if (bytes.Length < length)
                throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        private static void Require(Span<byte> bytes, int length)
        {
            if (bytes.Length < length)
                throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        [Transpose.Template("Browser.BinaryPrimitives.At({bytes}, {index})")]
        private static extern byte At(ReadOnlySpan<byte> bytes, int index);

        [Transpose.Template("Browser.BinaryPrimitives.Put({bytes}, {index}, {value})")]
        private static extern void Put(Span<byte> bytes, int index, byte value);
    }
}
#endif
