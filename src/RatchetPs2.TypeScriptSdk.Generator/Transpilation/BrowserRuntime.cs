using System.Diagnostics;

namespace RatchetPs2.TypeScriptSdk.Generator;

internal static class BrowserRuntime
{
    public static void Write(string projectDirectory, string output)
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(projectDirectory, "node_modules", "typescript", "bin", "tsc"));
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(Path.Combine(projectDirectory, "Runtime", "tsconfig.json"));
        start.ArgumentList.Add("--outDir");
        start.ArgumentList.Add(Path.Combine(output, "runtime"));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Node.js is required to compile the browser runtime.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Browser runtime TypeScript compilation failed. Run npm ci in the generator project if dependencies are missing.\n{stdout.Result}{stderr.Result}");
    }
}
