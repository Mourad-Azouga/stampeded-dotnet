using System.IO.Compression;
using System.Xml.Linq;

namespace Stampeded.Verify;

internal static class PackageVerifier
{
    public static void Verify(string root, string packagePath)
    {
        using var package = ZipFile.OpenRead(packagePath);
        var files = package.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToArray();
        if (files.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != files.Length)
            throw new InvalidDataException("Package contains duplicate file entries.");

        var manifests = files.Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).ToArray();
        if (manifests.Length != 1)
            throw new InvalidDataException("Package must contain exactly one NuGet manifest.");
        using var metadataStream = manifests[0].Open();
        var metadata = XDocument.Load(metadataStream);
        if (metadata.Descendants().Any(element => element.Name.LocalName == "dependency"
            && string.Equals((string?)element.Attribute("id"), "Npgsql", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Npgsql must remain a test-only dependency.");

        var assets = files.Where(entry => entry.FullName.StartsWith("runtimes/", StringComparison.Ordinal)).ToArray();
        if (assets.Length == 0)
            throw new InvalidDataException("The core package contains no native library.");
        foreach (var asset in assets)
        {
            var parts = asset.FullName.Split('/');
            if (parts.Length != 4 || parts[2] != "native" || parts.Any(part => part is "" or "." or ".."))
                throw new InvalidDataException($"Invalid NuGet runtime asset path: {asset.FullName}");
            Compare(asset, Path.Combine(root, asset.FullName));
        }

        Dictionary<string, string> expected = new()
        {
            ["README.md"] = "README.md",
            ["licenses/LICENSE"] = "native/stampeded-ffi/LICENSE",
            ["native/upstream.json"] = "artifacts/native-source/upstream.json",
            ["native/Cargo.lock"] = "native/Cargo.lock",
            ["native/include/stampeded.h"] = "native/stampeded-ffi/include/stampeded.h",
            ["schema/schema.sql"] = "artifacts/native-source/schema.sql"
        };
        foreach (var (destination, source) in expected)
        {
            var entry = package.GetEntry(destination)
                ?? throw new InvalidDataException($"Package is missing {destination}.");
            Compare(entry, Path.Combine(root, source));
        }
        Console.WriteLine($"Verified {Path.GetFileName(packagePath)}: {assets.Length} native asset(s), matching upstream files, no Npgsql dependency.");
    }

    private static void Compare(ZipArchiveEntry entry, string source)
    {
        using var original = File.OpenRead(source);
        if (entry.Length != original.Length)
            throw new InvalidDataException($"Packaged file differs from its source: {entry.FullName}");
        using var packed = entry.Open();
        Span<byte> packedBuffer = stackalloc byte[8192];
        Span<byte> originalBuffer = stackalloc byte[8192];
        int count;
        while ((count = packed.Read(packedBuffer)) != 0)
        {
            original.ReadExactly(originalBuffer[..count]);
            if (!packedBuffer[..count].SequenceEqual(originalBuffer[..count]))
                throw new InvalidDataException($"Packaged file differs from its source: {entry.FullName}");
        }
        if (original.Position != original.Length)
            throw new InvalidDataException($"Packaged file is truncated: {entry.FullName}");
    }
}
