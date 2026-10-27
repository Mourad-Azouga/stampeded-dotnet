using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Stampeded.Verify;

internal static class UpstreamVerifier
{
    internal const string PreparedDirectory = "artifacts/native-source";

    public static void Prepare(string root, string metadataPath)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
        var packages = metadata.RootElement.GetProperty("packages").EnumerateArray().ToArray();
        var connector = packages.Single(package => package.GetProperty("name").GetString() == "stampeded-ffi");
        var core = packages.Single(package => package.GetProperty("name").GetString() == "stampeded");
        const string registry = "registry+https://github.com/rust-lang/crates.io-index";
        if (ReadProperty(core, "source") != registry)
            throw new InvalidDataException("The Stampeded engine must come from the published crates.io crate.");

        var coreId = ReadProperty(core, "id");
        var connectorId = ReadProperty(connector, "id");
        var node = metadata.RootElement.GetProperty("resolve").GetProperty("nodes").EnumerateArray()
            .Single(node => ReadProperty(node, "id") == connectorId);
        if (!node.GetProperty("dependencies").EnumerateArray().Any(dependency => dependency.GetString() == coreId))
            throw new InvalidDataException("The connector must depend directly on the published Stampeded engine.");

        using var origin = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "native/upstream.json")));
        if (ReadProperty(origin.RootElement, "coreCrate") != "stampeded"
            || ReadProperty(origin.RootElement, "ffiVersion") != ReadProperty(connector, "version"))
            throw new InvalidDataException("Connector provenance differs from the Cargo package.");

        var source = Path.GetDirectoryName(ReadProperty(core, "manifest_path"))!;
        var coreVersion = ReadProperty(core, "version");
        var lockedCore = File.ReadAllText(Path.Combine(root, "native/Cargo.lock")).Split("[[package]]")
            .Single(package => HasLockProperty(package, "name", "stampeded")
                && HasLockProperty(package, "version", coreVersion)
                && HasLockProperty(package, "source", registry));
        var checksum = Regex.Match(lockedCore, "(?m)^checksum = \"([a-f0-9]{64})\"\\r?$");
        if (!checksum.Success)
            throw new InvalidDataException("The locked Stampeded crate must have a registry checksum.");
        var schema = File.ReadAllBytes(Path.Combine(source, "schema/schema.sql"));
        var destination = Path.Combine(root, PreparedDirectory);
        Directory.CreateDirectory(destination);
        File.WriteAllBytes(Path.Combine(destination, "schema.sql"), schema);
        File.WriteAllText(Path.Combine(destination, "upstream.json"), JsonSerializer.Serialize(new
        {
            repository = ReadProperty(origin.RootElement, "repository"),
            ffiSourceCommit = ReadProperty(origin.RootElement, "ffiSourceCommit"),
            ffiVersion = ReadProperty(connector, "version"),
            coreCrate = "stampeded",
            coreVersion,
            coreChecksum = checksum.Groups[1].Value,
            coreSource = registry,
            cargoLockSha256 = HashFile(Path.Combine(root, "native/Cargo.lock")),
            inputsSha256 = HashInputs(root),
            schemaSha256 = Convert.ToHexStringLower(SHA256.HashData(schema))
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Verify(root);
    }

    public static void Verify(string root)
    {
        var directory = Path.Combine(root, PreparedDirectory);
        var provenance = Path.Combine(directory, "upstream.json");
        if (!File.Exists(provenance))
            throw new InvalidDataException("Published crate sources are not prepared. Run: bash scripts/prepare-native.sh");

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(provenance));
        var record = manifest.RootElement;
        if (ReadProperty(record, "cargoLockSha256") != HashFile(Path.Combine(root, "native/Cargo.lock"))
            || ReadProperty(record, "inputsSha256") != HashInputs(root))
            throw new InvalidDataException("Native inputs changed. Run: bash scripts/build-native.sh");
        if (ReadProperty(record, "schemaSha256") != HashFile(Path.Combine(directory, "schema.sql")))
            throw new InvalidDataException("The prepared schema differs from the resolved crate. Run: bash scripts/prepare-native.sh");
        Console.WriteLine($"Verified published stampeded {ReadProperty(record, "coreVersion")} and local C ABI connector.");
    }

    private static string HashInputs(string root)
    {
        var connector = Path.Combine(root, "native/stampeded-ffi");
        var files = Directory.EnumerateFiles(Path.Combine(connector, "src"), "*.rs", SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(root, "native/Cargo.toml"),
                Path.Combine(root, "native/upstream.json"),
                Path.Combine(connector, "Cargo.toml"),
                Path.Combine(connector, "LICENSE"),
                Path.Combine(connector, "include/stampeded.h")
            })
            .Order(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/') + "\0"));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string HashFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    private static bool HasLockProperty(string package, string name, string value) =>
        Regex.IsMatch(package, $"(?m)^{Regex.Escape(name)} = \"{Regex.Escape(value)}\"\\r?$");

    private static string ReadProperty(JsonElement manifest, string name)
    {
        if (!manifest.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidDataException($"Upstream provenance must contain a nonempty '{name}' string.");
        return property.GetString()!;
    }
}
