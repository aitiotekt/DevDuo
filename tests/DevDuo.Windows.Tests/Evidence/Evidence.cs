using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal static class Evidence
{
    internal static void Freeze(string output)
    {
        var repository = Path.GetFullPath(".");
        var sources = new Dictionary<string, string>();
        var binaries = new List<object>();
        foreach (var assembly in new[] { typeof(Evidence).Assembly, typeof(NativeWebView).Assembly })
        {
            var binary = assembly.Location;
            var pdb = Path.ChangeExtension(binary, ".pdb");
            using var stream = File.OpenRead(pdb);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();
            foreach (var handle in reader.Documents)
            {
                var document = reader.GetDocument(handle);
                var path = Path.GetFullPath(reader.GetString(document.Name));
                if (!path.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                var relative = Path.GetRelativePath(repository, path).Replace('\\', '/');
                if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                if (!relative.StartsWith("src/DevDuo/", StringComparison.Ordinal) &&
                    !relative.StartsWith("tests/DevDuo.Windows.Tests/", StringComparison.Ordinal)) continue;
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)) continue;
                var expected = Convert.ToHexStringLower(reader.GetBlobBytes(document.Hash));
                if (reader.GetGuid(document.HashAlgorithm) != new Guid("8829d00f-11b8-4213-878b-770e8597ac16") || Hash(path) != expected)
                    throw new InvalidDataException("Native acceptance binary/source mismatch; rebuild before running: " + relative);
                Copy(repository, output, relative, sources);
            }
            foreach (var file in new[] { binary, pdb })
            {
                var destination = Path.Combine(output, "binaries", Path.GetFileName(file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, false);
                binaries.Add(new { path = "binaries/" + Path.GetFileName(file), sha256 = Hash(file) });
            }
        }
        foreach (var relative in new[]
        {
            "src/DevDuo/WebAssets/Scripts/browser-observer.js", "src/DevDuo/WebAssets/Scripts/remote-debugging.js",
            "src/DevDuo/WebAssets/Pages/color-scroll.html", "src/DevDuo/WebAssets/Pages/welcome.html",
            "src/DevDuo/Localization/en.json", "src/DevDuo/Localization/zh-Hans.json",
            "src/DevDuo/App.axaml", "src/DevDuo/Styles/DevDuoStyles.axaml",
            "src/DevDuo/DevDuo.csproj", "src/DevDuo/app.manifest", "global.json", "mise.toml", "mise.lock",
            "tests/DevDuo.Windows.Tests/DevDuo.Windows.Tests.csproj", "tests/DevDuo.Windows.Tests/packages.lock.json"
        }) Copy(repository, output, relative, sources);
        foreach (var name in new[] { "Scripts/browser-observer.js", "Scripts/remote-debugging.js", "Pages/color-scroll.html", "Pages/welcome.html" })
            if (Hash(Path.Combine(AppContext.BaseDirectory, "WebAssets", name)) != sources["src/DevDuo/WebAssets/" + name])
                throw new InvalidDataException("Native acceptance fixture is stale; rebuild: " + name);
        foreach (var page in Directory.GetFiles(Path.Combine(repository, "tests/DevDuo.WebKitProbe/pages"), "*.html"))
            Copy(repository, output, Path.GetRelativePath(repository, page).Replace('\\', '/'), sources);
        var nativeBuild = Path.Combine(repository, "temp/build/windows-compositor/build-manifest.json");
        using (var manifest = JsonDocument.Parse(File.ReadAllText(nativeBuild)))
        {
            var binary = Path.Combine(AppContext.BaseDirectory, "DevDuo.Windows.Compositor.dll");
            if (Hash(binary) != manifest.RootElement.GetProperty("binarySha256").GetString())
                throw new InvalidDataException("Native compositor binary is stale; rebuild before acceptance.");
            foreach (var source in manifest.RootElement.GetProperty("sources").EnumerateArray())
            {
                var relative = source.GetProperty("path").GetString()!;
                if (Hash(Path.Combine(repository, relative)) != source.GetProperty("sha256").GetString())
                    throw new InvalidDataException("Native compositor source changed; rebuild: " + relative);
                Copy(repository, output, relative, sources);
            }
            var relativeBinary = "binaries/DevDuo.Windows.Compositor.dll";
            File.Copy(binary, Path.Combine(output, relativeBinary));
            binaries.Add(new { path = relativeBinary, sha256 = Hash(binary) });
        }
        foreach (var path in Directory.GetFiles(Path.Combine(repository, "config"), "*.json"))
        {
            var relative = "config/" + Path.GetFileName(path);
            Copy(repository, output, relative, sources);
            if (Hash(Path.Combine(AppContext.BaseDirectory, relative)) != sources[relative])
                throw new InvalidDataException("Native acceptance configuration is stale; rebuild: " + relative);
        }
        var runtimeRoot = Path.Combine(repository, "temp/build/windows-webkit-host");
        File.Copy(Path.Combine(runtimeRoot, "runtime-manifest.json"), Path.Combine(output, "runtime-manifest.json"));
        File.Copy(Path.Combine(runtimeRoot, "build-inputs.json"), Path.Combine(output, "runtime-build-inputs.json"));
        using (var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "runtime-manifest.json"))))
        {
            foreach (var input in runtime.RootElement.GetProperty("inputFiles").EnumerateArray())
            {
                var relative = input.GetProperty("path").GetString()!;
                var source = Path.GetFullPath(Path.Combine(runtimeRoot, "inputs", relative));
                if (!source.StartsWith(Path.GetFullPath(Path.Combine(runtimeRoot, "inputs")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    Hash(source) != input.GetProperty("sha256").GetString())
                    throw new InvalidDataException("Frozen WebKit build input mismatch: " + relative);
                var destination = Path.Combine(output, "runtime-inputs", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
        }
        File.WriteAllText(Path.Combine(output, "build-evidence.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, compilerCSharpSourceChecksumsVerified = true, binaries,
            sources = sources.Select(file => new { path = file.Key, sha256 = file.Value }),
            nativeAcceptancePassed = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void Copy(string repository, string output, string relative, Dictionary<string, string> sources)
    {
        var source = Path.Combine(repository, relative);
        var destination = Path.Combine(output, "sources", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, false);
        sources.Add(relative, Hash(source));
    }
}
