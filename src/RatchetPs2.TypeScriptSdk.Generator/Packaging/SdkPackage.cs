using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace RatchetPs2.TypeScriptSdk.Generator;

internal sealed class SdkPackage
{
    private readonly string[] jsonIgnoredMembers;
    private readonly NullabilityInfoContext nullability = new();
    private readonly HashSet<Type> models = [];
    private readonly HashSet<Type> visiting = [];
    private readonly Dictionary<Type, InputModel> inputModels = [];
    private readonly Dictionary<Type, string> inputFactories = [];
    private readonly HashSet<Type> inputVisiting = [];
    private readonly HashSet<Type> instanceHandled = [];
    private readonly HashSet<Type> interfaceDeclarations = [];
    private readonly Dictionary<Type, int> instanceMethodCounts = [];
    private readonly List<string> declarations = [];
    private readonly List<string> normalizers = [];
    private readonly List<string> projections = [];
    private readonly List<string> bindings = [];
    private readonly List<string> modules = [];
    private int inputFactoryCount;
    private SdkPackage(IEnumerable<Type> serializationTypes) => jsonIgnoredMembers = serializationTypes
        .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => property.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Text.Json.Serialization.JsonIgnoreAttribute"))
            .Select(property => type.FullName + ":" + property.Name))
        .Order(StringComparer.Ordinal).ToArray();
    public List<string> Errors { get; } = [];
    public List<string> Exports { get; } = [];
    public string Source => "namespace RatchetPs2.JavaScript { public static class GeneratedExports {\n" + string.Join("\n", bindings) + "\n" + """
        public static global::System.IO.Stream CreateMemoryStream(byte[] bytes) { var value = new global::System.IO.MemoryStream(); value.Write(bytes, 0, bytes.Length); value.Position = 0; return value; }
        public static long StreamPosition(global::System.IO.Stream value) => value.Position;
        public static long StreamLength(global::System.IO.Stream value) => value.Length;
        public static long StreamSeek(global::System.IO.Stream value, long offset, int origin) => value.Seek(offset, (global::System.IO.SeekOrigin)origin);
        public static byte[] StreamBytes(global::System.IO.Stream value) => ((global::System.IO.MemoryStream)value).ToArray();
        public static object JsonDocumentValue(global::RatchetPs2.JavaScript.JsonDocument value) => value.RootElement.Export();
        public static object JsonElementValue(global::RatchetPs2.JavaScript.JsonElement value) => value.Export();
        #if MOBY_INTERFACES
        public static global::RatchetPs2.Core.Moby.IMobyModelInput CreateMobyModelInput(object value) => new global::RatchetPs2.JavaScript.BrowserMobyModelInput(value);
        public static global::RatchetPs2.Core.Moby.IMobyModelOutput CreateMobyModelOutput(object value) => new global::RatchetPs2.JavaScript.BrowserMobyModelOutput(value);
        #endif
        """ + "\n} }";

    public static SdkPackage Discover(IEnumerable<Type> types, IEnumerable<Type> serializationTypes)
    {
        var package = new SdkPackage(serializationTypes);
        foreach (var type in types.OrderBy(t => t.FullName, StringComparer.Ordinal)) package.Add(type);
        return package;
    }

    private void Add(Type type)
    {
        if (type.ContainsGenericParameters) { Errors.Add($"{type}: open generic exports are not supported."); return; }
        if (IsMobyModelInterface(type))
        {
            EnsureInterfaceDeclaration(type);
            instanceHandled.Add(type);
        }
        if (!type.IsAbstract && !type.IsEnum &&
            (type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length > 0 || type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length > 0))
        {
            try { Describe(type); }
            catch (NotSupportedException ex) { Errors.Add($"{type.FullName}: {ex.Message}"); }
        }
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName).OrderBy(m => m.ToString(), StringComparer.Ordinal).ToArray();
        var js = new List<string>();
        var ts = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            try
            {
                if (method.IsGenericMethod) throw new NotSupportedException("Generic methods need a concrete export signature.");
                var parameters = method.GetParameters();
                var outCount = method.ReturnType == typeof(bool) ? parameters.Reverse().TakeWhile(p => p.IsOut).Count() : 0;
                var tryRead = outCount > 0;
                var inputs = tryRead ? parameters[..^outCount] : parameters;
                var outputs = tryRead ? parameters[^outCount..] : [];
                if (inputs.Any(p => p.ParameterType.IsByRef || p.IsOut)) throw new NotSupportedException("ref/out inputs are not supported.");
                var resultType = tryRead ? outputs[0].ParameterType.GetElementType()! : method.ReturnType;
                var resultNullability = tryRead ? nullability.Create(outputs[0]) : nullability.Create(method.ReturnParameter);
                var resultTs = outCount > 1
                    ? "readonly [success: boolean, " + string.Join(", ", outputs.Select(p =>
                        Camel(p.Name!) + ": " + Describe(p.ParameterType.GetElementType()!, nullability.Create(p)) +
                        (!p.ParameterType.GetElementType()!.IsValueType && nullability.Create(p).ReadState != NullabilityState.Nullable ? " | null" : ""))) + "]"
                    : Describe(resultType, resultNullability);
                if (outCount == 1 && !resultTs.Contains(" | null", StringComparison.Ordinal)) resultTs += " | null";
                var name = Camel(method.Name);
                if (methods.Count(m => m.Name == method.Name) > 1)
                    name += "__" + string.Join("_", parameters.Select(p => Token(p.ParameterType)));
                if (!names.Add(name)) throw new NotSupportedException($"JavaScript export name collision: {name}.");
                var args = inputs.Select((p, i) => "a" + i).ToArray();
                var inputTypes = inputs.Select(p => InputType(p.ParameterType, nullability.Create(p))).ToArray();
                var jsArgs = inputs.Select((p, i) => args[i] + (p.HasDefaultValue ? " = " + Literal(p.DefaultValue) : ""));
                var checks = inputs.Select((p, i) => NormalizeInput(p.ParameterType, args[i], nullability.Create(p)));
                var callArgs = inputs.Select((p, i) => AdaptArgument(p.ParameterType, args[i]) ??
                    (IsBytes(p.ParameterType) && p.ParameterType != typeof(byte[])
                    ? p.ParameterType.GetGenericTypeDefinition() == typeof(Span<>)
                        ? $"global::System.ByteSpanExtensions.AsSpan({args[i]})"
                        : $"({CSharp(p.ParameterType)}){args[i]}" : args[i])).ToList();
                if (tryRead) callArgs.AddRange(outputs.Select((_, index) => "out var value" + index));
                var call = $"{CSharp(type)}.@{method.Name}({string.Join(", ", callArgs)})";
                var binding = "Call" + bindings.Count;
                var bindingParameters = string.Join(", ", inputs.Select((p, i) => BindingType(p.ParameterType) + " " + args[i]));
                if (outCount > 1)
                {
                    var tupleType = "global::System.ValueTuple<" + string.Join(", ", new[] { "bool" }.Concat(outputs.Select(p => CSharp(p.ParameterType.GetElementType()!)))) + ">";
                    bindings.Add($"public static {tupleType} {binding}({bindingParameters}) {{ var success = {call}; return new {tupleType}(success, {string.Join(", ", outputs.Select((_, index) => "value" + index))}); }}");
                }
                else
                {
                    var bindingReturn = CSharp(resultType) + (tryRead && resultType.IsValueType ? "?" : "");
                    bindings.Add($"public static {bindingReturn} {binding}({bindingParameters}) => " + (tryRead ? $"{call} ? value0 : null" : call) + ";");
                }
                var returnValue = outCount > 1
                    ? "return [value.Item1, " + string.Join(", ", outputs.Select((p, index) => Project(p.ParameterType.GetElementType()!, $"value.Item{index + 2}", nullability.Create(p), !p.ParameterType.GetElementType()!.IsValueType))) + "];"
                    : resultType == typeof(void) ? "" : "return " + Project(resultType, "value", resultNullability, tryRead) + ";";
                js.Add($"  {name}({string.Join(", ", jsArgs)}) {{\n{string.Join("\n", checks)}\n    const value = globalThis.RatchetPs2.JavaScript.GeneratedExports.{binding}({string.Join(", ", args)});\n    {returnValue}\n  }}");
                ts.Add($"  {name}({string.Join(", ", inputs.Select((p, i) => args[i] + (p.HasDefaultValue ? "?" : "") + ": " + inputTypes[i]))}): {resultTs};");
                Exports.Add($"{type.FullName}.{method}");
            }
            catch (NotSupportedException ex) { Errors.Add($"{type.FullName}.{method}: {ex.Message}"); }
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(f => f.IsLiteral))
        {
            try
            {
                if (!names.Add(Camel(field.Name))) throw new NotSupportedException($"JavaScript export name collision: {Camel(field.Name)}.");
                ts.Add($"  readonly {Camel(field.Name)}: {Describe(field.FieldType, nullability.Create(field))};");
                js.Add($"  {Camel(field.Name)}: {Literal(field.GetRawConstantValue())}");
            }
            catch (NotSupportedException ex) { Errors.Add($"{type.FullName}.{field.Name}: {ex.Message}"); }
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            try
            {
                if (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0)
                    throw new NotSupportedException("Static property is not publicly readable.");
                var name = Camel(property.Name);
                if (!names.Add(name)) throw new NotSupportedException($"JavaScript export name collision: {name}.");
                var info = nullability.Create(property);
                var resultTs = Describe(property.PropertyType, info);
                var binding = "Call" + bindings.Count;
                bindings.Add($"public static {CSharp(property.PropertyType)} {binding}() => {CSharp(type)}.@{property.Name};");
                js.Add($"  get {name}() {{\n    const value = globalThis.RatchetPs2.JavaScript.GeneratedExports.{binding}();\n    return {Project(property.PropertyType, "value", info)};\n  }}");
                ts.Add($"  readonly {name}: {resultTs};");
                Exports.Add($"{type.FullName}.{property.Name}");
            }
            catch (NotSupportedException ex) { Errors.Add($"{type.FullName}.{property.Name}: {ex.Message}"); }
        }
        if (models.Contains(type) && instanceMethodCounts.GetValueOrDefault(type) > 0)
        {
            try
            {
                const string name = "createInstance";
                if (!names.Add(name)) throw new NotSupportedException($"JavaScript export name collision: {name}.");
                var inputType = InputType(type);
                var check = NormalizeInput(type, "value", null);
                js.Add($"  {name}(value) {{\n{check}\n    return project_{Name(type)}(value);\n  }}");
                ts.Add($"  {name}(value: {inputType}): {Name(type)};");
                Exports.Add($"{type.FullName}.#createInstance");
            }
            catch (NotSupportedException ex) { Errors.Add($"{type.FullName}.#createInstance: {ex.Message}"); }
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(f => !f.IsLiteral))
            Errors.Add($"{type.FullName}.{field.Name}: static fields need a JavaScript state binding.");
        if (!instanceHandled.Contains(type) && type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(m => !m.IsSpecialName && !m.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))))
            Errors.Add($"{type.FullName}: instance methods need an explicit JavaScript object-lifetime binding.");
        if (js.Count == 0) return;
        declarations.Add($"export const {Name(type)}: {{\n{string.Join("\n", ts)}\n}};");
        modules.Add($"export const {Name(type)} = {{\n{string.Join(",\n", js)}\n}};");
    }

    private string Describe(Type type, NullabilityInfo? info = null)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return Describe(nullable) + " | null";
        var suffix = !type.IsValueType && info?.ReadState == NullabilityState.Nullable ? " | null" : "";
        if (type == typeof(void)) return "void";
        if (type == typeof(object)) return "JsonValue" + suffix;
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(string)) return "string" + suffix;
        if (IsJson(type)) return "JsonValue";
        if (IsStream(type)) return "MemoryStream" + suffix;
        if (type == typeof(long) || type == typeof(ulong)) return "bigint";
        if (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr) && type != typeof(char)) return "number";
        if (IsBytes(type)) return (type == typeof(byte[]) ? "Uint8Array" :
            type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) ? "Readonly<ByteSpan>" : "ByteSpan") + suffix;
        if (IsByteMemory(type)) return "Uint8Array" + suffix;
        if (IsDelegate(type)) return "(" + DelegateType(type, info) + ")" + suffix;
        if (IsMobyModelInterface(type)) { EnsureInterfaceDeclaration(type); return Name(type) + suffix; }
        if (NumericMembers(type) is { } numericMembers)
        {
            if (models.Add(type))
            {
                declarations.Add($"export interface {Name(type)} {{\n" + string.Join("\n", numericMembers.Select(member =>
                    $"  readonly {Camel(member)}: number;")) + "\n}");
                projections.Add($"function project_{Name(type)}(value) {{ return {{ " + string.Join(", ", numericMembers.Select(member =>
                    $"{Camel(member)}: value.{member}")) + " }; }");
            }
            return Name(type) + suffix;
        }
        if (Dictionary(type) is { } dictionary) return $"ReadonlyMap<{Describe(dictionary.Key, info?.GenericTypeArguments.ElementAtOrDefault(0))}, {Describe(dictionary.Value, info?.GenericTypeArguments.ElementAtOrDefault(1))}>" + suffix;
        if (SetElement(type) is { } setElement) return $"ReadonlySet<{Describe(setElement, info?.GenericTypeArguments.FirstOrDefault())}>" + suffix;
        if (TupleElements(type) is { } tuple) return "readonly [" + string.Join(", ", tuple.Select((element, index) => Describe(element, info?.GenericTypeArguments.ElementAtOrDefault(index)))) + "]" + suffix;
        if (type.IsArray && type.GetArrayRank() != 1) throw new NotSupportedException($"Multidimensional array {type} needs a shape-preserving mapping.");
        if (Element(type) is { } element) return $"ReadonlyArray<{Describe(element, info?.ElementType ?? info?.GenericTypeArguments.FirstOrDefault())}>" + suffix;
        if (type.IsEnum) return Describe(Enum.GetUnderlyingType(type));
        if (!IsDomain(type) || type.IsInterface || type.IsGenericType)
            throw new NotSupportedException($"No JavaScript value mapping for {type}.");
        if (visiting.Contains(type)) throw new NotSupportedException($"Cyclic object model {type} needs a reference-preserving mapping.");
        if (!models.Contains(type))
        {
            visiting.Add(type);
            try
            {
                var members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => p.GetIndexParameters().Length == 0 && p.GetMethod?.IsPublic == true
                        ? (p.Name, Type: p.PropertyType, Info: nullability.Create(p))
                        : throw new NotSupportedException($"Indexer or unreadable property {type}.{p.Name}."))
                    .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => (f.Name, Type: f.FieldType, Info: nullability.Create(f)))).ToArray();
                if (members.Select(p => Camel(p.Name)).Distinct(StringComparer.Ordinal).Count() != members.Length)
                    throw new NotSupportedException($"CamelCase field name collision in {type}.");
                var instance = InstanceMethods(type);
                var fields = members.Select(p => $"  readonly {Camel(p.Name)}: {Describe(p.Type, p.Info)};").Concat(instance.TypeScript).ToArray();
                var values = members.Select(p => $"  get {Camel(p.Name)}() {{ return {Project(p.Type, "value." + p.Name, p.Info)}; }}").Concat(instance.JavaScript);
                projections.Add($"function project_{Name(type)}(value) {{ return retainNative({{\n" + string.Join(",\n", values) + "\n}, value); }");
                declarations.Add($"export interface {Name(type)} {{\n  readonly [nativeValueBrand]: true;\n{string.Join("\n", fields)}\n}}");
                models.Add(type);
            }
            finally { visiting.Remove(type); }
        }
        return Name(type) + suffix;
    }

    private string Project(Type type, string value, NullabilityInfo? info = null, bool forceNullable = false)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var actual = underlying ?? type;
        if (actual.IsEnum) actual = Enum.GetUnderlyingType(actual);
        var result = IsStream(actual) ? $"projectMemoryStream({value})" :
            IsJson(actual) ? $"globalThis.RatchetPs2.JavaScript.GeneratedExports.{(IsJsonElement(actual) ? "JsonElementValue" : "JsonDocumentValue")}({value})" :
            actual == typeof(object) ? $"jsonValue({value}, false)" :
            IsBytes(actual) ? (actual == typeof(byte[]) ? $"Uint8Array.from({value})" : $"globalThis.System.ByteSpanExtensions.Export({value})") :
            IsByteMemory(actual) ? $"Uint8Array.from(globalThis.System.ByteMemoryExtensions.Export({value}))" :
            actual == typeof(long) || actual == typeof(ulong) ? $"BigInt({value}.toString())" :
            Dictionary(actual) is { } dictionary ? $"new Map(globalThis.Transpose.toArray({value}).map(item => [{Project(dictionary.Key, "item.Key", info?.GenericTypeArguments.ElementAtOrDefault(0))}, {Project(dictionary.Value, "item.Value", info?.GenericTypeArguments.ElementAtOrDefault(1))}]))" :
            SetElement(actual) is { } setElement ? $"new Set(globalThis.Transpose.toArray({value}).map(item => {Project(setElement, "item", info?.GenericTypeArguments.FirstOrDefault())}))" :
            TupleElements(actual) is { } tuple ? "[" + string.Join(", ", tuple.Select((element, index) => Project(element, $"{value}.Item{index + 1}", info?.GenericTypeArguments.ElementAtOrDefault(index)))) + "]" :
            Element(actual) is { } element ? $"globalThis.Transpose.toArray({value}).map(item => {Project(element, "item", info?.ElementType ?? info?.GenericTypeArguments.FirstOrDefault())})" :
            models.Contains(actual) || visiting.Contains(actual) ? $"project_{Name(actual)}({value})" : value;
        return forceNullable || underlying is not null || !type.IsValueType && info?.ReadState == NullabilityState.Nullable
            ? $"({value} == null ? null : {result})" : result;
    }

    private string InputType(Type type, NullabilityInfo? info = null)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying) return InputType(underlying) + " | null";
        var suffix = !type.IsValueType && info?.ReadState == NullabilityState.Nullable ? " | null" : "";
        if (IsStream(type) || IsBinaryReader(type) || IsBinaryWriter(type)) return "StreamInput" + suffix;
        if (IsJsonElement(type)) return "JsonValue";
        if (IsBytes(type)) return (type == typeof(byte[]) ? "Uint8Array | ArrayBuffer" :
            type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) ? "Readonly<ByteSpan> | ArrayBuffer" : "ByteSpan | ArrayBuffer") + suffix;
        if (IsByteMemory(type)) return "Uint8Array | ArrayBuffer" + suffix;
        if (IsDelegate(type)) return "(" + DelegateType(type, info) + ")" + suffix;
        if (IsMobyModelInterface(type)) { EnsureInterfaceDeclaration(type); return Name(type) + suffix; }
        if (type == typeof(object)) return "JsonValue" + suffix;
        if (NumericMembers(type) is not null) return Name(type) + suffix;
        if (Dictionary(type) is { } dictionary) return $"ReadonlyMap<{InputType(dictionary.Key, info?.GenericTypeArguments.ElementAtOrDefault(0))}, {InputType(dictionary.Value, info?.GenericTypeArguments.ElementAtOrDefault(1))}>" + suffix;
        if (SetElement(type) is { } setElement) return $"ReadonlySet<{InputType(setElement, info?.GenericTypeArguments.FirstOrDefault())}>" + suffix;
        if (TupleElements(type) is { } tuple) return "readonly [" + string.Join(", ", tuple.Select((element, index) => InputType(element, info?.GenericTypeArguments.ElementAtOrDefault(index)))) + "]" + suffix;
        if (Element(type) is { } element) return $"ReadonlyArray<{InputType(element, info?.ElementType ?? info?.GenericTypeArguments.FirstOrDefault())}>" + suffix;
        if (type == typeof(string)) return "string" + suffix;
        if (type == typeof(bool)) return "boolean";
        if (type.IsEnum) return InputType(Enum.GetUnderlyingType(type));
        if (type == typeof(long) || type == typeof(ulong)) return "bigint";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(float) || type == typeof(double)) return "number";
        if (IsDomain(type) && !type.IsInterface && !type.IsGenericType)
        {
            try { return EnsureInputModel(type).TypeScriptName + (models.Contains(type) ? " | " + Name(type) : "") + suffix; }
            catch (NotSupportedException)
            {
                Describe(type, info);
                return Name(type) + suffix;
            }
        }
        throw new NotSupportedException($"No JavaScript input mapping for {type}.");
    }

    private string NormalizeInput(Type type, string arg, NullabilityInfo? info)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        var actualType = nullable ?? type;
        if (actualType == typeof(object)) return $"    {arg} = normalizeJson({arg});";
        if (IsMobyModelInterface(actualType))
        {
            EnsureInterfaceDeclaration(actualType);
            var methods = actualType.GetMethods().Where(method => !method.IsSpecialName).Select(method => Camel(method.Name)).ToArray();
            var invalid = string.Join(" || ", methods.Select(method => $"typeof {arg}.{method} !== 'function'"));
            var factory = actualType.Name == "IMobyModelInput" ? "CreateMobyModelInput" : "CreateMobyModelOutput";
            return $"    if ({arg} === null || typeof {arg} !== 'object' || {invalid}) throw new TypeError('Invalid {actualType.Name} argument {arg}.');\n" +
                $"    {arg} = globalThis.RatchetPs2.JavaScript.GeneratedExports.{factory}({arg});";
        }
        if (IsDelegate(actualType))
        {
            var invoke = actualType.GetMethod("Invoke")!;
            var parameters = invoke.GetParameters();
            var callback = arg + "_callback";
            var args = parameters.Select((_, index) => "p" + index).ToArray();
            var genericInfo = info?.GenericTypeArguments ?? [];
            var callArgs = parameters.Select((parameter, index) => Project(parameter.ParameterType, args[index],
                genericInfo.ElementAtOrDefault(index) ?? nullability.Create(parameter))).ToArray();
            var returnInfo = genericInfo.ElementAtOrDefault(parameters.Length) ?? nullability.Create(invoke.ReturnParameter);
            var body = invoke.ReturnType == typeof(void)
                ? $"{callback}({string.Join(", ", callArgs)});"
                : $"let callbackResult = {callback}({string.Join(", ", callArgs)});\n" +
                  NormalizeInput(invoke.ReturnType, "callbackResult", returnInfo) +
                  "\n      return callbackResult;";
            var normalize = $"    if (typeof {arg} !== 'function') throw new TypeError('Invalid callback argument {arg}.');\n" +
                $"    const {callback} = {arg};\n    {arg} = ({string.Join(", ", args)}) => {{\n      {body.Replace("\n", "\n      ")}\n    }};";
            return info?.ReadState == NullabilityState.Nullable
                ? $"    if ({arg} !== null) {{\n{Indent(normalize, 2)}\n    }}" : normalize;
        }
        if (IsJsonElement(actualType)) return $"    {arg} = normalizeJson({arg});";
        if (IsStream(actualType) || IsBinaryReader(actualType) || IsBinaryWriter(actualType))
        {
            var normalize = $"    if (nativeValues.has({arg})) {arg} = nativeValues.get({arg});\n" +
                $"    else {{\n      if ({arg} instanceof ArrayBuffer) {arg} = new Uint8Array({arg});\n" +
                $"      if (!({arg} instanceof Uint8Array)) throw new TypeError('Expected a memory stream or binary buffer for {arg}.');\n" +
                $"      {arg} = globalThis.RatchetPs2.JavaScript.GeneratedExports.CreateMemoryStream({arg});\n    }}";
            return info?.ReadState == NullabilityState.Nullable
                ? $"    if ({arg} !== null) {{\n{Indent(normalize, 2)}\n    }}" : normalize;
        }
        if (NumericMembers(actualType) is { } members)
        {
            var checks = string.Join(" || ", members.Select(member => $"typeof {arg}.{Camel(member)} !== 'number'"));
            var normalize = $"    if ({arg} === null || typeof {arg} !== 'object' || {checks}) throw new TypeError('Invalid {actualType.Name} argument {arg}.');\n" +
                $"    {arg} = [{string.Join(", ", members.Select(member => $"Math.fround({arg}.{Camel(member)})"))}];";
            return nullable is not null ? $"    if ({arg} !== null) {{\n{normalize}\n    }}" : normalize;
        }
        if (IsBytes(type))
        {
            var view = type == typeof(byte[]) ? "" : $" || globalThis.System.ByteSpanExtensions.IsView({arg})";
            var check = $"    if ({arg} instanceof ArrayBuffer) {arg} = new Uint8Array({arg});\n    if (!({arg} instanceof Uint8Array{view})) throw new TypeError('Expected a binary buffer for {arg}.');";
            return type == typeof(byte[]) && info?.ReadState == NullabilityState.Nullable ? $"    if ({arg} !== null) {{\n{check}\n    }}" : check;
        }
        if (IsByteMemory(type))
        {
            var check = $"    if ({arg} instanceof ArrayBuffer) {arg} = new Uint8Array({arg});\n    if (!({arg} instanceof Uint8Array)) throw new TypeError('Expected a binary buffer for {arg}.');";
            return info?.ReadState == NullabilityState.Nullable ? $"    if ({arg} !== null) {{\n{check}\n    }}" : check;
        }
        if (Dictionary(type) is { } dictionary)
        {
            var factory = EnsureDictionaryFactory(type, dictionary.Key, dictionary.Value);
            var keyInfo = info?.GenericTypeArguments.ElementAtOrDefault(0);
            var valueInfo = info?.GenericTypeArguments.ElementAtOrDefault(1);
            var normalize = $"    if (!({arg} instanceof Map)) throw new TypeError('Invalid map argument {arg}.');\n" +
                $"    const {arg}_keys = [];\n    const {arg}_values = [];\n    for (const [entryKey, entryValue] of {arg}) {{\n" +
                $"      let itemKey = entryKey;\n{Indent(NormalizeInput(dictionary.Key, "itemKey", keyInfo), 2)}\n" +
                $"      let itemValue = entryValue;\n{Indent(NormalizeInput(dictionary.Value, "itemValue", valueInfo), 2)}\n" +
                $"      {arg}_keys.push(itemKey);\n      {arg}_values.push(itemValue);\n    }}\n" +
                $"    {arg} = globalThis.RatchetPs2.JavaScript.GeneratedExports.{factory}({arg}_keys, {arg}_values);";
            return info?.ReadState == NullabilityState.Nullable ? $"    if ({arg} !== null) {{\n{Indent(normalize, 2)}\n    }}" : normalize;
        }
        if (SetElement(type) is { } setElement)
        {
            var factory = EnsureSetFactory(type, setElement);
            var item = NormalizeInput(setElement, "item", info?.GenericTypeArguments.FirstOrDefault());
            var normalize = $"    if (!({arg} instanceof Set)) throw new TypeError('Invalid set argument {arg}.');\n" +
                $"    const {arg}_values = [];\n    for (const entryValue of {arg}) {{\n      let item = entryValue;\n{Indent(item, 2)}\n      {arg}_values.push(item);\n    }}\n" +
                $"    {arg} = globalThis.RatchetPs2.JavaScript.GeneratedExports.{factory}({arg}_values);";
            return info?.ReadState == NullabilityState.Nullable ? $"    if ({arg} !== null) {{\n{Indent(normalize, 2)}\n    }}" : normalize;
        }
        if (TupleElements(type) is { } tuple)
        {
            var factory = EnsureTupleFactory(type, tuple);
            var checks = new List<string> { $"    if (!Array.isArray({arg}) || {arg}.length !== {tuple.Length}) throw new TypeError('Invalid tuple argument {arg}.');" };
            var values = tuple.Select((element, index) =>
            {
                var value = arg + "_" + index;
                checks.Add($"    let {value} = {arg}[{index}];");
                checks.Add(NormalizeInput(element, value, info?.GenericTypeArguments.ElementAtOrDefault(index)));
                return value;
            }).ToArray();
            checks.Add($"    {arg} = globalThis.RatchetPs2.JavaScript.GeneratedExports.{factory}({string.Join(", ", values)});");
            var normalize = string.Join("\n", checks);
            return nullable is not null ? $"    if ({arg} !== null) {{\n{Indent(normalize, 2)}\n    }}" : normalize;
        }
        if (Element(type) is { } element)
        {
            var elementInfo = info?.ElementType ?? info?.GenericTypeArguments.FirstOrDefault();
            var item = NormalizeInput(element, "item", elementInfo).Replace("    ", "      ");
            var normalize = $"    if (!Array.isArray({arg})) throw new TypeError('Invalid collection argument {arg}.');\n" +
                $"    {arg} = Array.from({arg});\n    for (let i = 0; i < {arg}.length; i++) {{\n      let item = {arg}[i];\n{item}\n      {arg}[i] = item;\n    }}";
            return info?.ReadState == NullabilityState.Nullable ? $"    if ({arg} !== null) {{\n{normalize}\n    }}" : normalize;
        }
        if (inputModels.TryGetValue(actualType, out var inputModel))
        {
            var normalize = $"    {arg} = normalize_{inputModel.TypeScriptName}({arg});";
            return nullable is not null || !type.IsValueType && info?.ReadState == NullabilityState.Nullable
                ? $"    if ({arg} !== null) {{\n{Indent(normalize, 2)}\n    }}" : normalize;
        }
        if (models.Contains(actualType))
        {
            var normalize = $"    if (!nativeValues.has({arg})) throw new TypeError('Expected an SDK-owned {actualType.Name} value for {arg}.');\n    {arg} = nativeValues.get({arg});";
            return nullable is not null || !type.IsValueType && info?.ReadState == NullabilityState.Nullable
                ? $"    if ({arg} !== null) {{\n{Indent(normalize, 2)}\n    }}" : normalize;
        }
        var actual = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
        if (actual == typeof(long) || actual == typeof(ulong))
            return $"    if (typeof {arg} !== 'bigint' || {arg} < {(actual == typeof(ulong) ? "0n" : "-9223372036854775808n")} || {arg} > {(actual == typeof(ulong) ? "18446744073709551615n" : "9223372036854775807n")}) throw new TypeError('Invalid {actual.Name} argument {arg}.');";
        var condition = actual == typeof(bool) ? $"typeof {arg} !== 'boolean'" : actual == typeof(string) ? $"typeof {arg} !== 'string'" : $"typeof {arg} !== 'number'";
        if (actual != typeof(bool) && actual != typeof(string) && actual != typeof(float) && actual != typeof(double))
        {
            var min = actual == typeof(byte) || actual == typeof(ushort) || actual == typeof(uint) ? "0" : actual == typeof(sbyte) ? "-128" : actual == typeof(short) ? "-32768" : "-2147483648";
            var max = actual == typeof(byte) ? "255" : actual == typeof(sbyte) ? "127" : actual == typeof(ushort) ? "65535" : actual == typeof(short) ? "32767" : actual == typeof(uint) ? "4294967295" : "2147483647";
            condition = $"!Number.isInteger({arg}) || {arg} < {min} || {arg} > {max}";
        }
        if (actual == typeof(string) && info?.ReadState == NullabilityState.Nullable) condition = $"{arg} !== null && ({condition})";
        return $"    if ({condition}) throw new TypeError('Invalid {actual.Name} argument {arg}.');" +
            (actual == typeof(float) ? $"\n    {arg} = Math.fround({arg});" : "");
    }

    public void Write(string output)
    {
        File.WriteAllText(Path.Combine(output, "index.d.ts"), """
            declare const byteSpanBrand: unique symbol;
            declare const nativeValueBrand: unique symbol;
            export type JsonValue = null | boolean | number | string | readonly JsonValue[] | { readonly [key: string]: JsonValue };
            /** Shared view over a C# array. Use Uint8Array.from(view) for an owned copy. */
            export interface ByteSpanView {
              readonly [byteSpanBrand]: true;
              readonly length: number;
              [index: number]: number;
            }
            export type ByteSpan = Uint8Array | ByteSpanView;
            export interface MemoryStream {
              readonly [nativeValueBrand]: true;
              readonly position: bigint;
              readonly length: bigint;
              seek(offset: bigint, origin?: 0 | 1 | 2): bigint;
              toArray(): Uint8Array;
            }
            export type StreamInput = MemoryStream | Uint8Array | ArrayBuffer;
            export declare function createMemoryStream(bytes?: Uint8Array | ArrayBuffer): MemoryStream;

            """ + string.Join("\n\n", declarations) + "\n");
        File.WriteAllText(Path.Combine(output, "index.js"), """
            import './ratchetps2.js';
            import { installRuntime, nativeValues, retainNative, projectMemoryStream } from './runtime/streams.js';
            import { configureJson, normalizeJson, jsonValue } from './runtime/json.js';
            export { createMemoryStream } from './runtime/streams.js';
            installRuntime();
            configureJson([JSON_IGNORED_MEMBERS]);

            """.Replace("JSON_IGNORED_MEMBERS", string.Join(", ", jsonIgnoredMembers.Select(member => JsonSerializer.Serialize(member)))) +
            string.Join("\n\n", normalizers.Concat(projections).Concat(modules)) + "\n");
        File.WriteAllText(Path.Combine(output, "package.json"), """
            { "name": "@ratchetps2/sdk", "version": "0.0.0", "private": true,
              "type": "module", "dependencies": { "fflate": "0.8.3" },
              "exports": { ".": { "types": "./index.d.ts", "import": "./index.js" } } }
            """);
    }

    private static bool IsBytes(Type type) => type == typeof(byte[]) || type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) || type.GetGenericTypeDefinition() == typeof(Span<>)) && type.GenericTypeArguments[0] == typeof(byte);
    private static bool IsByteMemory(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>) && type.GenericTypeArguments[0] == typeof(byte);
    private static bool IsStream(Type type) => type == typeof(Stream) || type == typeof(MemoryStream);
    private static bool IsBinaryReader(Type type) => type == typeof(BinaryReader);
    private static bool IsBinaryWriter(Type type) => type == typeof(BinaryWriter);
    private static bool IsDelegate(Type type) => typeof(Delegate).IsAssignableFrom(type);
    private static bool IsMobyModelInterface(Type type) => type.FullName is "RatchetPs2.Core.Moby.IMobyModelInput" or "RatchetPs2.Core.Moby.IMobyModelOutput";
    private static bool IsJsonElement(Type type) => type.FullName == "System.Text.Json.JsonElement";
    private static bool IsJson(Type type) => type.FullName is "System.Text.Json.JsonElement" or "System.Text.Json.JsonDocument";
    private static bool IsDomain(Type type) => type.Namespace is { } name && (name == "RatchetPs2" || name.StartsWith("RatchetPs2.", StringComparison.Ordinal));
    private static (Type Key, Type Value)? Dictionary(Type type) => type.IsGenericType &&
        new[] { typeof(Dictionary<,>), typeof(IReadOnlyDictionary<,>), typeof(IDictionary<,>) }.Contains(type.GetGenericTypeDefinition())
        ? (type.GenericTypeArguments[0], type.GenericTypeArguments[1]) : null;
    private static Type? SetElement(Type type) => type.IsGenericType &&
        new[] { typeof(HashSet<>), typeof(IReadOnlySet<>), typeof(ISet<>) }.Contains(type.GetGenericTypeDefinition())
        ? type.GenericTypeArguments[0] : null;
    private static Type[]? TupleElements(Type type) => type.IsGenericType && type.Namespace == "System" && type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal)
        ? type.GenericTypeArguments : null;
    private static Type? Element(Type type) => type.IsArray ? type.GetElementType() : type.IsGenericType &&
        new[] { typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IEnumerable<>), typeof(List<>) }.Contains(type.GetGenericTypeDefinition()) ? type.GenericTypeArguments[0] : null;
    private static string[]? NumericMembers(Type type) => type.FullName switch
    {
        "System.Numerics.Vector2" => ["X", "Y"],
        "System.Numerics.Vector3" => ["X", "Y", "Z"],
        "System.Numerics.Vector4" or "System.Numerics.Quaternion" => ["X", "Y", "Z", "W"],
        "System.Numerics.Matrix4x4" => ["M11", "M12", "M13", "M14", "M21", "M22", "M23", "M24",
            "M31", "M32", "M33", "M34", "M41", "M42", "M43", "M44"],
        _ => null
    };
    private static string BindingType(Type type)
    {
        if (IsMobyModelInterface(type)) return CSharp(type);
        if (IsJsonElement(type)) return "object";
        if (IsBinaryReader(type) || IsBinaryWriter(type)) return "global::System.IO.Stream";
        if (IsBytes(type) || IsByteMemory(type)) return "byte[]";
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (NumericMembers(actual) is not null) return "float[]";
        if (Element(type) is { } element) return BindingType(element) + "[]";
        return CSharp(type);
    }
    private static string? AdaptArgument(Type type, string argument)
    {
        if (IsJsonElement(type)) return $"new global::RatchetPs2.JavaScript.JsonElement({argument})";
        if (IsBinaryReader(type)) return $"new global::RatchetPs2.JavaScript.BinaryReader({argument}, global::System.Text.Encoding.UTF8, true)";
        if (IsBinaryWriter(type)) return $"new global::RatchetPs2.JavaScript.BinaryWriter({argument}, global::System.Text.Encoding.UTF8, true)";
        if (IsByteMemory(type)) return $"new global::System.ReadOnlyMemory<byte>({argument})";
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (NumericMembers(actual) is { } members)
        {
            var value = $"new {CSharp(actual)}({string.Join(", ", members.Select((_, index) => $"{argument}[{index}]"))})";
            return actual == type ? value : $"{argument} == null ? ({CSharp(type)})null : {value}";
        }
        if (Element(type) is not { } element) return null;
        var converted = NumericMembers(element) is not null
            ? $"global::RatchetPs2.JavaScript.NumericInputs.{element.Name}Array({argument})"
            : argument;
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            ? $"new {CSharp(type)}({converted})" : converted;
    }

    private string DelegateType(Type type, NullabilityInfo? info)
    {
        var invoke = type.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters();
        var genericInfo = info?.GenericTypeArguments ?? [];
        var result = invoke.ReturnType == typeof(void)
            ? "void"
            : InputType(invoke.ReturnType, genericInfo.ElementAtOrDefault(parameters.Length) ?? nullability.Create(invoke.ReturnParameter));
        return "(" + string.Join(", ", parameters.Select((parameter, index) =>
            "a" + index + ": " + Describe(parameter.ParameterType, genericInfo.ElementAtOrDefault(index) ?? nullability.Create(parameter)))) + ") => " + result;
    }

    private void EnsureInterfaceDeclaration(Type type)
    {
        if (!interfaceDeclarations.Add(type)) return;
        declarations.Add($"export interface {Name(type)} {{\n" + string.Join("\n", type.GetMethods()
            .Where(method => !method.IsSpecialName)
            .OrderBy(method => method.MetadataToken)
            .Select(method => $"  {Camel(method.Name)}({string.Join(", ", method.GetParameters().Select((parameter, index) =>
                "a" + index + (parameter.HasDefaultValue ? "?" : "") + ": " + Describe(parameter.ParameterType, nullability.Create(parameter))))}): " +
                (method.ReturnType == typeof(void) ? "void" : InputType(method.ReturnType, nullability.Create(method.ReturnParameter))) + ";")) + "\n}");
    }

    private (string[] TypeScript, string[] JavaScript) InstanceMethods(Type type)
    {
        if (!instanceHandled.Add(type)) return ([], []);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && !method.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .OrderBy(method => method.ToString(), StringComparer.Ordinal).ToArray();
        var ts = new List<string>();
        var js = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            try
            {
                if (method.IsGenericMethod) throw new NotSupportedException("Generic methods need a concrete export signature.");
                var parameters = method.GetParameters();
                if (parameters.Any(parameter => parameter.ParameterType.IsByRef || parameter.IsOut))
                    throw new NotSupportedException("Instance ref/out parameters are not supported.");
                var resultInfo = nullability.Create(method.ReturnParameter);
                var resultTs = method.ReturnType == type ? Name(type) : Describe(method.ReturnType, resultInfo);
                var name = Camel(method.Name);
                if (methods.Count(candidate => candidate.Name == method.Name) > 1)
                    name += "__" + string.Join("_", parameters.Select(parameter => Token(parameter.ParameterType)));
                if (!names.Add(name)) throw new NotSupportedException($"JavaScript method name collision: {name}.");
                var args = parameters.Select((_, index) => "a" + index).ToArray();
                var inputTypes = parameters.Select(parameter => InputType(parameter.ParameterType, nullability.Create(parameter))).ToArray();
                var jsArgs = parameters.Select((parameter, index) => args[index] + (parameter.HasDefaultValue ? " = " + Literal(parameter.DefaultValue) : ""));
                var checks = parameters.Select((parameter, index) => NormalizeInput(parameter.ParameterType, args[index], nullability.Create(parameter)));
                var callArgs = parameters.Select((parameter, index) => AdaptArgument(parameter.ParameterType, args[index]) ??
                    (IsBytes(parameter.ParameterType) && parameter.ParameterType != typeof(byte[])
                        ? parameter.ParameterType.GetGenericTypeDefinition() == typeof(Span<>)
                            ? $"global::System.ByteSpanExtensions.AsSpan({args[index]})"
                            : $"({CSharp(parameter.ParameterType)}){args[index]}" : args[index]));
                var binding = "Call" + bindings.Count;
                bindings.Add($"public static {CSharp(method.ReturnType)} {binding}({CSharp(type)} value{(parameters.Length == 0 ? "" : ", ")}{string.Join(", ", parameters.Select((parameter, index) => BindingType(parameter.ParameterType) + " " + args[index]))}) => value.@{method.Name}({string.Join(", ", callArgs)});");
                var returnValue = method.ReturnType == typeof(void) ? "" : "return " +
                    (method.ReturnType == type ? $"project_{Name(type)}(result)" : Project(method.ReturnType, "result", resultInfo)) + ";";
                js.Add($"  {name}({string.Join(", ", jsArgs)}) {{\n{string.Join("\n", checks)}\n    const result = globalThis.RatchetPs2.JavaScript.GeneratedExports.{binding}(value{(args.Length == 0 ? "" : ", " + string.Join(", ", args))});\n    {returnValue}\n  }}");
                ts.Add($"  {name}({string.Join(", ", parameters.Select((parameter, index) => args[index] + (parameter.HasDefaultValue ? "?" : "") + ": " + inputTypes[index]))}): {resultTs};");
                Exports.Add($"{type.FullName}.{method}");
                instanceMethodCounts[type] = instanceMethodCounts.GetValueOrDefault(type) + 1;
            }
            catch (NotSupportedException ex) { Errors.Add($"{type.FullName}.{method}: {ex.Message}"); }
        }
        return (ts.ToArray(), js.ToArray());
    }

    private InputModel EnsureInputModel(Type type)
    {
        if (inputModels.TryGetValue(type, out var existing)) return existing;
        if (!inputVisiting.Add(type)) throw new NotSupportedException($"Cyclic input model {type} needs a reference-preserving mapping.");
        try
        {
            var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length).ThenBy(c => c.MetadataToken).FirstOrDefault();
            InputMember[] members;
            var propertyInitializer = constructor is not null && constructor.GetParameters().Length == 0;
            if (propertyInitializer)
            {
                members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true)
                    .Select((p, index) => new InputMember(index, p.Name, p.PropertyType, nullability.Create(p), null,
                        !p.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute)))).ToArray();
                if (members.Length == 0) throw new NotSupportedException($"{type} has no public constructor parameters or writable properties.");
            }
            else
            {
                if (constructor is null) throw new NotSupportedException($"{type} has no public constructor for JavaScript values.");
                members = constructor.GetParameters().Select((p, index) =>
                    new InputMember(index, p.Name!, p.ParameterType, nullability.Create(p), p, false)).ToArray();
            }
            foreach (var member in members) InputType(member.Type, member.Info);
            var factory = "Input" + inputFactoryCount++;
            var model = new InputModel(Name(type) + "Input", factory, members);
            inputModels.Add(type, model);
            declarations.Add($"export interface {model.TypeScriptName} {{\n" + string.Join("\n", members.Select(member =>
                $"  readonly {member.JavaScriptName}{(member.OptionalProperty || member.Parameter?.HasDefaultValue == true ? "?" : "")}: {InputType(member.Type, member.Info)};")) + "\n}");
            if (propertyInitializer)
            {
                var parameters = members.SelectMany(member => member.OptionalProperty
                    ? new[] { $"bool h{member.Index}", $"{BindingType(member.Type)} a{member.Index}" }
                    : new[] { $"{BindingType(member.Type)} a{member.Index}" });
                var assignments = members.Select(member => $"@{member.Name} = " + (member.OptionalProperty ? $"h{member.Index} ? " : "") +
                    (AdaptArgument(member.Type, "a" + member.Index) ?? "a" + member.Index) + (member.OptionalProperty ? $" : defaults.@{member.Name}" : ""));
                var defaults = members.Any(member => member.OptionalProperty)
                    ? $"var defaults = new {CSharp(type)} {{ {string.Join(", ", members.Where(member => !member.OptionalProperty).Select(member => $"@{member.Name} = default"))} }}; "
                    : "";
                bindings.Add($"public static {CSharp(type)} {factory}({string.Join(", ", parameters)}) {{ {defaults}return new {CSharp(type)} {{ {string.Join(", ", assignments)} }}; }}");
            }
            else
            {
                var parameters = members.Select(member => $"{BindingType(member.Type)} a{member.Index}");
                var arguments = members.Select(member => AdaptArgument(member.Type, "a" + member.Index) ?? "a" + member.Index);
                bindings.Add($"public static {CSharp(type)} {factory}({string.Join(", ", parameters)}) => new {CSharp(type)}({string.Join(", ", arguments)});");
            }
            normalizers.Add(ModelNormalizer(type, model));
            return model;
        }
        finally { inputVisiting.Remove(type); }
    }

    private string ModelNormalizer(Type type, InputModel model)
    {
        const string arg = "value";
        var values = new List<string>();
        var checks = new List<string>
        {
            $"  if ({arg} === null || typeof {arg} !== 'object' || Array.isArray({arg})) throw new TypeError('Invalid {type.Name} argument {arg}.');"
        };
        foreach (var member in model.Members)
        {
            var value = "item_" + member.Index;
            if (member.OptionalProperty)
            {
                var present = value + "Present";
                checks.Add($"  const {present} = Object.prototype.hasOwnProperty.call({arg}, '{member.JavaScriptName}');");
                checks.Add($"  let {value} = {present} ? {arg}.{member.JavaScriptName} : {Placeholder(member.Type)};");
                checks.Add($"  if ({present}) {{\n{NormalizeInput(member.Type, value, member.Info)}\n  }}");
                values.Add(present);
            }
            else
            {
                checks.Add($"  let {value} = {arg}.{member.JavaScriptName};");
                if (member.Parameter?.HasDefaultValue == true)
                    checks.Add($"  if ({value} === undefined) {value} = {Literal(member.Parameter.DefaultValue)};");
                checks.Add(NormalizeInput(member.Type, value, member.Info));
            }
            values.Add(value);
        }
        checks.Add($"  return globalThis.RatchetPs2.JavaScript.GeneratedExports.{model.Factory}({string.Join(", ", values)});");
        return $"function normalize_{model.TypeScriptName}({arg}) {{\n  if (nativeValues.has({arg})) return nativeValues.get({arg});\n{string.Join("\n", checks)}\n}}";
    }

    private string EnsureDictionaryFactory(Type type, Type key, Type value)
    {
        if (inputFactories.TryGetValue(type, out var existing)) return existing;
        var factory = "Input" + inputFactoryCount++;
        inputFactories.Add(type, factory);
        var keyValue = AdaptArgument(key, "keys[i]") ?? "keys[i]";
        var itemValue = AdaptArgument(value, "values[i]") ?? "values[i]";
        bindings.Add($"public static {CSharp(type)} {factory}({BindingType(key)}[] keys, {BindingType(value)}[] values) {{ var result = new global::System.Collections.Generic.Dictionary<{CSharp(key)}, {CSharp(value)}>(); for (var i = 0; i < keys.Length; i++) result.Add({keyValue}, {itemValue}); return result; }}");
        return factory;
    }

    private string EnsureSetFactory(Type type, Type element)
    {
        if (inputFactories.TryGetValue(type, out var existing)) return existing;
        var factory = "Input" + inputFactoryCount++;
        inputFactories.Add(type, factory);
        var item = AdaptArgument(element, "values[i]") ?? "values[i]";
        bindings.Add($"public static {CSharp(type)} {factory}({BindingType(element)}[] values) {{ var result = new global::System.Collections.Generic.HashSet<{CSharp(element)}>(); for (var i = 0; i < values.Length; i++) result.Add({item}); return result; }}");
        return factory;
    }

    private string EnsureTupleFactory(Type type, Type[] elements)
    {
        if (inputFactories.TryGetValue(type, out var existing)) return existing;
        var factory = "Input" + inputFactoryCount++;
        inputFactories.Add(type, factory);
        var parameters = elements.Select((element, index) => $"{BindingType(element)} a{index}");
        var arguments = elements.Select((element, index) => AdaptArgument(element, "a" + index) ?? "a" + index);
        bindings.Add($"public static {CSharp(type)} {factory}({string.Join(", ", parameters)}) => new {CSharp(type)}({string.Join(", ", arguments)});");
        return factory;
    }

    private static string Placeholder(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (!actual.IsValueType || IsBytes(type) || NumericMembers(actual) is not null || Element(type) is not null) return "null";
        return actual == typeof(bool) ? "false" : "0";
    }

    private static string Indent(string value, int spaces) => string.Join("\n", value.Split('\n').Select(line => new string(' ', spaces) + line));

    private sealed record InputModel(string TypeScriptName, string Factory, InputMember[] Members);
    private sealed record InputMember(int Index, string Name, Type Type, NullabilityInfo Info, ParameterInfo? Parameter, bool OptionalProperty)
    {
        public string JavaScriptName => Camel(Name);
    }
    private static string Name(Type type) => type.FullName!.Replace('.', '_').Replace('+', '_');
    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
    private static string Token(Type type) => type.IsByRef ? "out_" + Token(type.GetElementType()!) : type.IsArray ? Token(type.GetElementType()!) + "Array" :
        type.IsGenericType ? type.Name.Split('`')[0] + "Of" + string.Join("And", type.GenericTypeArguments.Select(Token)) : type.Name;
    private static string CSharp(Type type) => type.FullName switch
    {
        "System.Text.Json.JsonDocument" => "global::RatchetPs2.JavaScript.JsonDocument",
        "System.Text.Json.JsonElement" => "global::RatchetPs2.JavaScript.JsonElement",
        _ => type == typeof(void) ? "void" : type.IsArray ? CSharp(type.GetElementType()!) + "[]" :
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlySet<>)
        ? "global::System.Collections.Generic.ISet<" + CSharp(type.GenericTypeArguments[0]) + ">" : type.IsGenericType
        ? "global::" + type.GetGenericTypeDefinition().FullName!.Split('`')[0].Replace('+', '.') + "<" + string.Join(", ", type.GenericTypeArguments.Select(CSharp)) + ">"
        : "global::" + type.FullName!.Replace('+', '.')
    };
    private static string Literal(object? value) => value is long or ulong ? Convert.ToString(value, CultureInfo.InvariantCulture) + "n" :
        value is float or double && !double.IsFinite(Convert.ToDouble(value)) ? Convert.ToDouble(value).ToString(CultureInfo.InvariantCulture) : JsonSerializer.Serialize(value);
}
