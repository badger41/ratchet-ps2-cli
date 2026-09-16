using System.IO.Enumeration;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RatchetPs2.TypeScriptSdk.Generator;
using Transpose.Compiler.Library;

var directory = new DirectoryInfo(AppContext.BaseDirectory);
while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RatchetPs2.TypeScriptSdk.Generator.csproj"))) directory = directory.Parent;
if (directory is null) throw new InvalidOperationException("Cannot locate the SDK build project.");
var root = Path.GetFullPath("../..", directory.FullName);
var output = Path.Combine(directory.FullName, "bin/probe");
var filters = new List<string>();
var verify = false;
string? uyaWad = null;
string? uyaZip = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--type" && i + 1 < args.Length) filters.Add(args[++i]);
    else if (args[i] == "--output" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
    else if (args[i] == "--verify") verify = true;
    else if (args[i] == "--verify-uya" && i + 2 < args.Length)
    {
        uyaWad = Path.GetFullPath(args[++i]);
        uyaZip = Path.GetFullPath(args[++i]);
    }
    else { Console.Error.WriteLine("Usage: dotnet run -- [--output DIRECTORY] [--type 'Namespace.TypePattern']... [--verify] [--verify-uya WAD ZIP]"); return 2; }
}
Directory.CreateDirectory(output);
// A failed build must never leave an importable package from a previous selection.
foreach (var file in new[]
{
    "ratchetps2.js", "index.js", "index.d.ts", "package.json", "report.json",
    // Left by the original per-type prototype and early buffer probe.
    "PifHeader.js", "PifHeader.errors.txt", "BinarySpanReader.errors.txt", "buffer-consumer.ts"
}) File.Delete(Path.Combine(output, file));
try
{
    BrowserRuntime.Write(directory.FullName, output);
    var sources = ProjectSources.Load(root);
    var assembly = sources.LoadAssembly();
    var publicTypes = assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
    foreach (var filter in filters)
        if (!publicTypes.Any(t => FileSystemName.MatchesSimpleExpression(filter, t.FullName!, ignoreCase: false)))
            throw new ArgumentException($"No public C# type matches '{filter}'.");
    var selected = publicTypes.Where(t => filters.Count == 0 || filters.Any(f => FileSystemName.MatchesSimpleExpression(f, t.FullName!, ignoreCase: false))).ToArray();
    IEnumerable<SyntaxTree> sourceFiles = filters.Count == 0
        ? sources.Files.Values.OrderBy(t => t.FilePath, StringComparer.Ordinal)
        : sources.Dependencies(selected);
    var files = sourceFiles.Where(tree => File.Exists(tree.FilePath)).ToArray();
    var package = SdkPackage.Discover(selected, assembly.GetTypes());
    var unusedImports = sources.Compilation.GetDiagnostics().Where(d => d.Id == "CS8019")
        .GroupBy(d => d.Location.SourceTree).ToDictionary(g => g.Key!, g => g.Select(d => d.Location.SourceSpan).ToHashSet());
    var request = new CompilationRequest("RatchetPs2.JavaScript")
        .WithPackageReference("Transpose.BCL", "26.9.4872")
        .WithSourceFile("BrowserCompatibility.cs", File.ReadAllText(Path.Combine(directory.FullName, "Transpilation", "BrowserCompatibility.cs")))
        .WithSourceFile("GeneratedExports.cs", package.Source)
        .WithRuntime()
        .WithoutReflection();
    if (files.Any(tree => Path.GetFileName(tree.FilePath) is "IMobyModelInput.cs" or "IMobyModelOutput.cs"))
        request.WithDefine("MOBY_INTERFACES");
    foreach (var tree in files)
    {
        // Preserve project imports and conditional symbols; change only the browser target branch.
        foreach (var define in ((CSharpParseOptions)tree.Options).PreprocessorSymbolNames) request.WithDefine(define);
        var normalized = new BrowserSourceRewriter(sources.Compilation.GetSemanticModel(tree), unusedImports.GetValueOrDefault(tree, []))
            .Visit(tree.GetRoot())!;
        request.WithSourceFile(tree.FilePath, "using InvalidDataException = RatchetPs2.JavaScript.InvalidDataException;\n" + normalized.ToFullString());
    }
    Console.WriteLine($"Discovered {sources.Projects.Count} projects, {sources.Files.Count} files and {publicTypes.Length} public types. Compiling {files.Length} files for {selected.Length} selected types.");
    var result = TransposeCompilerLibrary.Compile(request);
    var errors = package.Errors.Concat(result.Errors).ToArray();
    File.WriteAllLines(Path.Combine(output, "errors.txt"), errors);
    var report = JsonSerializer.Serialize(new
    {
        success = result.Success && package.Errors.Count == 0,
        fullLibrary = filters.Count == 0,
        projects = sources.Projects.Select(p => Path.GetRelativePath(root, p)).Order(StringComparer.Ordinal),
        discoveredFiles = sources.Files.Count,
        discoveredPublicTypes = publicTypes.Length,
        selectedTypes = selected.Select(t => t.FullName),
        compiledFiles = files.Select(t => Path.GetRelativePath(root, t.FilePath)),
        exports = package.Exports,
        apiErrors = package.Errors,
        compilerErrors = result.Errors
    }, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(output, "report.json"), report);
    if (filters.Count == 0) File.WriteAllText(Path.Combine(output, "full-library-report.json"), report);
    if (!result.Success || package.Errors.Count != 0)
    {
        Console.Error.WriteLine($"Build failed: {package.Errors.Count} API mapping errors; {result.Errors.Count} compiler errors. See {Path.Combine(output, "report.json")}.");
        foreach (var error in errors.Take(8)) Console.Error.WriteLine(error);
        return 1;
    }
    // ponytail: one SDK version per realm; isolate runtime globals if side-by-side versions are needed.
    var javascript = result.Javascript!.Replace("globals = global;", "globals = globalThis;", StringComparison.Ordinal);
    File.WriteAllText(Path.Combine(output, "ratchetps2.js"), "import * as Browser from './runtime/index.js';\n(function () {\n" + javascript + "\n}).call(globalThis);\n");
    package.Write(output);
    foreach (var name in new[] { "ratchetps2.js", "index.js" })
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--check");
        start.ArgumentList.Add(Path.Combine(output, name));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Node.js is required to validate emitted JavaScript.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Invalid emitted JavaScript in {name}: {stderr.Result}{stdout.Result}");
    }
    if (verify) Verification.Write(output, assembly);
    if (uyaWad is not null && uyaZip is not null)
        Verification.WriteUyaEndToEnd(output, assembly, uyaWad, uyaZip);
    Console.WriteLine($"Built {package.Exports.Count} exports in {output}.");
    return 0;
}
catch (Exception ex)
{
    foreach (var name in new[] { "ratchetps2.js", "index.js", "index.d.ts", "package.json" }) File.Delete(Path.Combine(output, name));
    var reportPath = Path.Combine(output, "report.json");
    var report = File.Exists(reportPath) ? JsonNode.Parse(File.ReadAllText(reportPath))! : new JsonObject();
    report["success"] = false;
    report["fullLibrary"] = filters.Count == 0;
    report["buildError"] = ex.Message;
    var failedReport = report.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(reportPath, failedReport);
    if (filters.Count == 0) File.WriteAllText(Path.Combine(output, "full-library-report.json"), failedReport);
    File.WriteAllText(Path.Combine(output, "errors.txt"), ex.ToString());
    Console.Error.WriteLine(ex.Message);
    return 1;
}
