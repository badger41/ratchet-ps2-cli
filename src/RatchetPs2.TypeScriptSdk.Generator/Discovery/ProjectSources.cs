using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RatchetPs2.TypeScriptSdk.Generator;

internal sealed class ProjectSources
{
    private readonly HashSet<string> analyzers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SyntaxTree> files = new(StringComparer.Ordinal);
    private readonly List<string> projects = [];
    private readonly HashSet<string> references = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SyntaxTree> Files => files;
    public IReadOnlyList<string> Projects => projects;
    public CSharpCompilation Compilation { get; private set; } = null!;

    public static ProjectSources Load(string root)
    {
        var sources = new ProjectSources();
        var src = Path.Combine(root, "src");
        var projects = Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(p => Path.GetFileNameWithoutExtension(p) is "RatchetPs2.Core" or "RatchetPs2.Sdk" ||
                Path.GetFileNameWithoutExtension(p).StartsWith("RatchetPs2.Games.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (var project in projects) sources.ReadProject(project, root);
        if (sources.projects.Count == 0) throw new InvalidOperationException("No SDK projects found.");

        // Bind against the projects' .NET reference assemblies, never the transpiler's partial BCL.
        var projectNames = sources.projects.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var references = sources.references.Where(p => !projectNames.Contains(Path.GetFileNameWithoutExtension(p)))
            .Select(p => MetadataReference.CreateFromFile(p));
        sources.Compilation = CSharpCompilation.Create("RatchetPs2.SdkDiscovery", sources.files.Values, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var generators = sources.analyzers.SelectMany(path =>
        {
            var reference = new AnalyzerFileReference(path, new GeneratorLoader());
            var failures = new List<string>();
            reference.AnalyzerLoadFailed += (_, e) => failures.Add(e.Message);
            var found = reference.GetGenerators(LanguageNames.CSharp);
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            return found;
        }).ToArray();
        if (generators.Length > 0)
        {
            CSharpGeneratorDriver.Create(generators, parseOptions: (CSharpParseOptions)sources.files.Values.First().Options)
                .RunGeneratorsAndUpdateCompilation(sources.Compilation, out var generated, out var diagnostics);
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error || d.Id is "CS8784" or "CS8785"))
                throw new InvalidOperationException(string.Join("\n", diagnostics));
            sources.Compilation = (CSharpCompilation)generated;
            foreach (var tree in generated.SyntaxTrees) sources.files[tree.FilePath] = tree;
        }
        return sources;
    }

    private void ReadProject(string project, string root)
    {
        project = Path.GetFullPath(project);
        if (projects.Contains(project, StringComparer.Ordinal)) return;
        projects.Add(project);
        var dotnetRoot = Path.GetFullPath("../../..", RuntimeEnvironment.GetRuntimeDirectory());
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["DOTNET_NOLOGO"] = "1";
        foreach (var arg in new[] { "msbuild", project, "-target:ResolveReferences", "-verbosity:quiet",
            "-getItem:Compile,ProjectReference,Using,PackageReference,Analyzer,ReferencePath", "-getProperty:DefineConstants" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start MSBuild.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"MSBuild failed for {project}: {stderr.Result}\n{stdout.Result}");
        using var document = JsonDocument.Parse(stdout.Result);
        var items = document.RootElement.GetProperty("Items");
        foreach (var analyzer in items.GetProperty("Analyzer").EnumerateArray()) analyzers.Add(analyzer.GetProperty("FullPath").GetString()!);
        foreach (var reference in items.GetProperty("ReferencePath").EnumerateArray()) references.Add(reference.GetProperty("FullPath").GetString()!);
        if (items.GetProperty("PackageReference").GetArrayLength() != 0)
            throw new NotSupportedException($"{project} has NuGet dependencies; provide a transpiler-compatible library mapping before including them.");
        var imports = new StringBuilder();
        foreach (var item in items.GetProperty("Using").EnumerateArray())
        {
            imports.Append("using ");
            if (item.TryGetProperty("Static", out var isStatic) && isStatic.GetString() == "true") imports.Append("static ");
            if (item.TryGetProperty("Alias", out var alias) && !string.IsNullOrEmpty(alias.GetString())) imports.Append(alias.GetString()).Append(" = ");
            imports.Append(item.GetProperty("Identity").GetString()).AppendLine(";");
        }
        var defines = document.RootElement.GetProperty("Properties").GetProperty("DefineConstants").GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var options = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: defines);
        if (files.Count > 0 && !defines.ToHashSet().SetEquals(((CSharpParseOptions)files.Values.First().Options).PreprocessorSymbolNames))
            throw new NotSupportedException($"{project} uses different conditional symbols; compile it separately to preserve its semantics.");
        var projectTrees = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        foreach (var item in items.GetProperty("Compile").EnumerateArray())
        {
            var path = item.GetProperty("FullPath").GetString()!;
            if (!File.Exists(path)) throw new FileNotFoundException("An evaluated Compile item is missing; run its source generation first.", path);
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path, Encoding.UTF8);
            var syntax = tree.GetCompilationUnitRoot();
            var usings = SyntaxFactory.ParseCompilationUnit(imports.ToString()).Usings;
            var combined = syntax.Usings.Where(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
                .Concat(usings).Concat(syntax.Usings.Where(u => !u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)));
            projectTrees[path] = tree.WithRootAndOptions(syntax.WithUsings(SyntaxFactory.List(combined)), options);
        }
        // Expand each project's globals before binding, preserving aliases and unused-import diagnostics.
        var globals = projectTrees.Values.SelectMany(t => t.GetCompilationUnitRoot().Usings)
            .Where(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).Select(u => u.WithGlobalKeyword(default)).ToArray();
        foreach (var (path, tree) in projectTrees)
        {
            var syntax = tree.GetCompilationUnitRoot();
            var expanded = globals.Concat(syntax.Usings.Where(u => !u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))
                .DistinctBy(u => u.WithoutTrivia().NormalizeWhitespace().ToFullString());
            var normalized = tree.WithRootAndOptions(syntax.WithUsings(SyntaxFactory.List(expanded)), tree.Options);
            if (files.TryGetValue(path, out var existing) && existing.GetText().ToString() != normalized.GetText().ToString())
                throw new NotSupportedException($"Linked source {path} has different imports between projects; compile those projects separately.");
            files[path] = normalized;
        }
        foreach (var reference in items.GetProperty("ProjectReference").EnumerateArray())
            ReadProject(reference.GetProperty("FullPath").GetString()!, root);
    }

    public Assembly LoadAssembly()
    {
        using var dll = new MemoryStream();
        var result = Compilation.Emit(dll);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return Assembly.Load(dll.ToArray());
    }

    public IReadOnlyList<SyntaxTree> Dependencies(IEnumerable<Type> roots)
    {
        var files = new HashSet<SyntaxTree>();
        var pending = new Queue<SyntaxTree>();
        void Include(ISymbol? symbol)
        {
            if (symbol is null or INamespaceSymbol) return;
            if (symbol is INamedTypeSymbol named) symbol = named.OriginalDefinition;
            foreach (var syntax in symbol.DeclaringSyntaxReferences)
                if (files.Add(syntax.SyntaxTree)) pending.Enqueue(syntax.SyntaxTree);
            if (symbol.ContainingType is not null) Include(symbol.ContainingType);
        }
        foreach (var type in roots) Include(Compilation.GetTypeByMetadataName(type.FullName!));
        // Conservative file closure includes every partial declaration and helper referenced by a file.
        while (pending.TryDequeue(out var tree))
        {
            var model = Compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is BaseTypeDeclarationSyntax declaration) Include(model.GetDeclaredSymbol(declaration));
                if (node is ExpressionSyntax or TypeSyntax)
                {
                    var info = model.GetSymbolInfo(node);
                    Include(info.Symbol);
                    foreach (var candidate in info.CandidateSymbols) Include(candidate);
                    Include(model.GetTypeInfo(node).Type);
                }
            }
        }
        return files.OrderBy(t => t.FilePath, StringComparer.Ordinal).ToArray();
    }

    private sealed class GeneratorLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) { } // LoadFrom resolves adjacent assembly dependencies.
        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }
}
