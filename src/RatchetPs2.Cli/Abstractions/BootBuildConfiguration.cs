using System.Text;

namespace RatchetPs2.Cli.Abstractions;

internal sealed record BootBuildConfiguration(string SourceIso, string BootElf, string OutputIso)
{
    public static BootBuildConfiguration Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inBuild = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('['))
            {
                if (!line.Equals("[build]", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unknown configuration section: {line}");
                inBuild = true;
                continue;
            }
            var separator = line.IndexOf('=');
            if (!inBuild || separator <= 0) throw new InvalidDataException($"Invalid configuration line: {line}");
            var key = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();
            if (key is not ("source_iso" or "boot_elf" or "output_iso") || value.Length == 0 || !values.TryAdd(key, value))
                throw new InvalidDataException($"Unknown, empty, or duplicate build key: {key}");
        }
        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Resolve(string key) => values.TryGetValue(key, out var value)
            ? Path.GetFullPath(value, root)
            : throw new InvalidDataException($"Missing configuration key: {key}");
        return new(Resolve("source_iso"), Resolve("boot_elf"), Resolve("output_iso"));
    }

    public static void Write(string path, string sourceIso, string bootElf, string outputIso)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Relative(string value)
        {
            if (value.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Configuration paths cannot contain newlines.");
            return Path.GetRelativePath(root, Path.GetFullPath(value));
        }
        var text = $"; Paths are resolved relative to this config.ini. Spaces need no quotes.\n" +
            "; Set boot_elf to your compiler's output; each build reads that file directly.\n" +
            $"[build]\nsource_iso={Relative(sourceIso)}\nboot_elf={Relative(bootElf)}\noutput_iso={Relative(outputIso)}\n";
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
