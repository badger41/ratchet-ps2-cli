using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (arguments.Length != 3)
{
    Console.Error.WriteLine("Usage: RatchetPs2.Wasm.Generator <manifestPath> <jsOutputPath> <dtsOutputPath>");
    return 1;
}

var manifestPath = arguments[0];
var jsOutputPath = arguments[1];
var dtsOutputPath = arguments[2];
var generatorProjectDirectory = AppContext.BaseDirectory;
for (var directory = new DirectoryInfo(generatorProjectDirectory); directory is not null; directory = directory.Parent)
{
    if (File.Exists(Path.Combine(directory.FullName, "RatchetPs2.Wasm.Generator.csproj")))
    {
        generatorProjectDirectory = directory.FullName;
        break;
    }
}
var templateDirectory = Path.Combine(generatorProjectDirectory ?? AppContext.BaseDirectory, "Templates");

var manifestJson = await File.ReadAllTextAsync(manifestPath);
var manifest = JsonSerializer.Deserialize<WasmManifest>(manifestJson, WasmManifestJsonContext.Default.WasmManifest)
    ?? throw new InvalidOperationException("Failed to deserialize WASM export manifest.");

var javaScriptTemplate = await File.ReadAllTextAsync(Path.Combine(templateDirectory, "ratchetps2-wasm.js.template"));
var typeScriptTemplate = await File.ReadAllTextAsync(Path.Combine(templateDirectory, "ratchetps2-wasm.d.ts.template"));

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsOutputPath))!);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dtsOutputPath))!);

await File.WriteAllTextAsync(jsOutputPath, GenerateJavaScript(manifest, javaScriptTemplate));
await File.WriteAllTextAsync(dtsOutputPath, GenerateTypeScriptDefinitions(manifest, typeScriptTemplate));

return 0;

static string GenerateJavaScript(WasmManifest manifest, string template)
{
    var builder = new StringBuilder();
    foreach (var export in manifest.Exports)
    {
        builder.AppendLine(RenderJavaScriptExport(export));
        builder.AppendLine();
    }

    return template.Replace("/*__GENERATED_EXPORTS__*/", builder.ToString().TrimEnd());
}

static string RenderJavaScriptExport(WasmExport export)
{
    var parameters = export.Parameters?.Count > 0
        ? string.Join(", ", export.Parameters.Select(p => p.Optional ? $"{p.Name} = {{}}" : p.Name))
        : string.Empty;

    var builder = new StringBuilder();
    builder.AppendLine($"export async function {export.JsMethodName}({parameters}) {{");

    if (export.JsMethodName is not "initializeRatchetPs2Wasm")
    {
        builder.AppendLine("  await ensureStarted();");
        builder.AppendLine();
    }

    if (export.CustomImplementation is { Count: > 0 })
    {
        foreach (var line in export.CustomImplementation)
        {
            builder.AppendLine($"  {line}");
        }

        builder.Append('}');
        return builder.ToString();
    }

    foreach (var line in export.Preamble ?? [])
    {
        builder.AppendLine($"  {line}");
    }

    var invokeArguments = export.InvokeArguments is { Count: > 0 }
        ? string.Join(",\n    ", export.InvokeArguments)
        : string.Empty;

    builder.AppendLine("  return DotNet.invokeMethodAsync(");
    builder.AppendLine("    \"RatchetPs2.Wasm\",");
    builder.AppendLine($"    \"{export.DotNetMethodName}\"{(invokeArguments.Length > 0 ? "," : string.Empty)}");
    if (invokeArguments.Length > 0)
    {
        builder.AppendLine($"    {invokeArguments}");
    }

    builder.AppendLine("  );");
    builder.Append('}');
    return builder.ToString();
}

static string GenerateTypeScriptDefinitions(WasmManifest manifest, string template)
{
    var builder = new StringBuilder();

    foreach (var sharedType in manifest.SharedTypes)
    {
        if (sharedType.Kind == "typeAlias")
        {
            builder.AppendLine($"export type {sharedType.Name} = {sharedType.Value};");
            builder.AppendLine();
            continue;
        }

        builder.AppendLine($"export interface {sharedType.Name} {{");
        foreach (var property in sharedType.Properties ?? [])
        {
            builder.AppendLine($"  {property.Name}{(property.Optional ? "?" : string.Empty)}: {property.Type};");
        }

        builder.AppendLine("}");
        builder.AppendLine();
    }

    builder.AppendLine($"export interface {manifest.InitOptionsTypeName} {{");
    foreach (var property in manifest.InitOptions)
    {
        builder.AppendLine($"  {property.Name}{(property.Optional ? "?" : string.Empty)}: {property.Type};");
    }
    builder.AppendLine("}");
    builder.AppendLine();
    builder.AppendLine($"export function configureRatchetPs2Wasm(options?: {manifest.InitOptionsTypeName}): void;");
    builder.AppendLine();
    builder.AppendLine($"export function initializeRatchetPs2Wasm(options?: {manifest.InitOptionsTypeName}): Promise<void>;");
    builder.AppendLine();

    foreach (var export in manifest.Exports)
    {
        var parameters = export.Parameters?.Count > 0
            ? string.Join(", ", export.Parameters.Select(p => $"{p.Name}{(p.Optional ? "?" : string.Empty)}: {p.Type}"))
            : string.Empty;
        builder.AppendLine($"export function {export.JsMethodName}({parameters}): {export.ReturnType};");
        builder.AppendLine();
    }

    return template.Replace("/*__GENERATED_TYPES__*/", builder.ToString().TrimEnd());
}

public sealed class WasmManifest
{
    public string InitOptionsTypeName { get; set; } = "RatchetPs2WasmInitOptions";
    public List<WasmProperty> InitOptions { get; set; } = [];
    public List<WasmSharedType> SharedTypes { get; set; } = [];
    public List<WasmExport> Exports { get; set; } = [];
}

public sealed class WasmSharedType
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
    public List<WasmProperty>? Properties { get; set; }
}

public sealed class WasmExport
{
    public string DotNetMethodName { get; set; } = string.Empty;
    public string JsMethodName { get; set; } = string.Empty;
    public string ReturnType { get; set; } = "Promise<void>";
    public List<WasmParameter>? Parameters { get; set; }
    public List<string>? Preamble { get; set; }
    public List<string>? InvokeArguments { get; set; }
    public List<string>? CustomImplementation { get; set; }
}

public class WasmProperty
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Optional { get; set; }
}

public sealed class WasmParameter : WasmProperty
{
    public bool OptionsObject { get; set; }
}

[JsonSerializable(typeof(WasmManifest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal partial class WasmManifestJsonContext : JsonSerializerContext
{
}