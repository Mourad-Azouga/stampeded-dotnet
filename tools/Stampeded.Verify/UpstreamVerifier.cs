using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stampeded.Verify;

internal static class UpstreamVerifier
{
    internal const string PreparedDirectory = "artifacts/native-source";
    private const string Registry = "registry+https://github.com/rust-lang/crates.io-index";

    public static string FfiVersion(string root) => ReadConfig(root, "ffiVersion");
    public static string FfiChecksum(string root) => ReadConfig(root, "ffiChecksum");

    public static void RefreshFfi(string root, string indexPath)
    {
        var current = FfiVersion(root);
        var series = Regex.Match(current, @"^\d+\.\d+\.").Value;
        if (series.Length == 0) throw new InvalidDataException("The pinned FFI version is invalid.");
        var releases = File.ReadLines(indexPath)
            .Select(line => JsonNode.Parse(line)!)
            .Where(entry => entry["name"]?.GetValue<string>() == "stampeded-ffi" &&
                entry["vers"]?.GetValue<string>().StartsWith(series, StringComparison.Ordinal) == true &&
                Regex.IsMatch(entry["vers"]!.GetValue<string>(), @"^\d+\.\d+\.\d+$") &&
                entry["yanked"]?.GetValue<bool>() == false)
            .OrderByDescending(entry => Version.Parse(entry["vers"]!.GetValue<string>()))
            .ToArray();
        if (releases.Length == 0) throw new InvalidDataException("No compatible published FFI release was found.");
        var configPath = Path.Combine(root, "native/upstream.json");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["ffiVersion"] = releases[0]["vers"]!.GetValue<string>();
        config["ffiChecksum"] = releases[0]["cksum"]!.GetValue<string>();
        File.WriteAllText(configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Pinned published stampeded-ffi {config["ffiVersion"]}.");
    }

    public static void Prepare(string root, string metadataPath)
    {
        var destination = Path.Combine(root, PreparedDirectory);
        var crate = Path.Combine(destination, "crate");
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
        var packages = metadata.RootElement.GetProperty("packages").EnumerateArray().ToArray();
        var ffi = packages.Single(package => ReadProperty(package, "name") == "stampeded-ffi");
        var core = packages.Single(package => ReadProperty(package, "name") == "stampeded");
        if (ReadProperty(ffi, "version") != FfiVersion(root) ||
            Path.GetFullPath(ReadProperty(ffi, "manifest_path")) != Path.GetFullPath(Path.Combine(crate, "Cargo.toml")) ||
            ReadProperty(core, "source") != Registry)
            throw new InvalidDataException("Resolved crates differ from pinned published FFI inputs.");

        var ffiId = ReadProperty(ffi, "id");
        var coreId = ReadProperty(core, "id");
        var node = metadata.RootElement.GetProperty("resolve").GetProperty("nodes").EnumerateArray()
            .Single(item => ReadProperty(item, "id") == ffiId);
        if (!node.GetProperty("dependencies").EnumerateArray().Any(item => item.GetString() == coreId))
            throw new InvalidDataException("The published FFI must depend directly on the published core.");

        var coreVersion = ReadProperty(core, "version");
        var coreSource = Path.GetDirectoryName(ReadProperty(core, "manifest_path"))!;
        var lockfile = Path.Combine(root, "native/Cargo.lock");
        if (HashFile(lockfile) != HashFile(Path.Combine(crate, "Cargo.lock")))
            throw new InvalidDataException("The staged Cargo lockfile differs from native/Cargo.lock.");
        var lockedCore = File.ReadAllText(lockfile).Split("[[package]]")
            .Single(package => HasLockProperty(package, "name", "stampeded") &&
                HasLockProperty(package, "version", coreVersion) &&
                HasLockProperty(package, "source", Registry));
        var checksum = Regex.Match(lockedCore, "(?m)^checksum = \"([a-f0-9]{64})\"\\r?$");
        if (!checksum.Success)
            throw new InvalidDataException("The published core must have a Cargo registry checksum.");

        var archive = Path.Combine(destination, $"stampeded-ffi-{FfiVersion(root)}.crate");
        if (HashFile(archive) != FfiChecksum(root))
            throw new InvalidDataException("The published FFI archive checksum differs from the pin.");
        File.Copy(Path.Combine(crate, "include/stampeded.h"), Path.Combine(destination, "stampeded.h"), true);
        File.Copy(Path.Combine(root, "native/LICENSE"), Path.Combine(destination, "LICENSE"), true);

        var schemaSource = Path.Combine(coreSource, "schema");
        var migrations = Directory.GetFiles(schemaSource, "*.sql").Order(StringComparer.Ordinal).ToArray();
        if (migrations.Length == 0 || migrations.Any(path => !Regex.IsMatch(Path.GetFileName(path), @"^\d{4}_[a-z0-9_]+\.sql$")))
            throw new InvalidDataException("The core crate must contain numbered SQL migrations.");
        var stagedMigrations = Path.Combine(destination, "migrations");
        if (Directory.Exists(stagedMigrations)) Directory.Delete(stagedMigrations, true);
        Directory.CreateDirectory(stagedMigrations);
        var sql = new StringBuilder("""
            -- Fresh install generated from the published stampeded migrations.
            -- Run the matching upstream daemon with --migrate for existing databases.
            CREATE TABLE stampeded_migrations (
                version BIGINT PRIMARY KEY,
                description TEXT NOT NULL,
                installed_on TIMESTAMPTZ NOT NULL DEFAULT now(),
                success BOOLEAN NOT NULL,
                checksum BYTEA NOT NULL,
                execution_time BIGINT NOT NULL
            );

            """);
        foreach (var migration in migrations)
        {
            var filename = Path.GetFileName(migration);
            var version = long.Parse(filename[..4], System.Globalization.CultureInfo.InvariantCulture);
            var description = filename[5..^4].Replace('_', ' ');
            var bytes = File.ReadAllBytes(migration);
            File.WriteAllBytes(Path.Combine(stagedMigrations, filename), bytes);
            sql.AppendLine($"-- {filename}");
            sql.AppendLine(Encoding.UTF8.GetString(bytes));
            sql.AppendLine($"INSERT INTO stampeded_migrations (version, description, success, checksum, execution_time) VALUES ({version}, '{description}', true, decode('{Convert.ToHexStringLower(SHA384.HashData(bytes))}', 'hex'), 0);");
        }
        File.WriteAllText(Path.Combine(destination, "schema.sql"), sql.ToString());
        File.WriteAllText(Path.Combine(destination, "upstream.json"), JsonSerializer.Serialize(new
        {
            repository = ReadConfig(root, "repository"),
            ffiCrate = "stampeded-ffi",
            ffiVersion = FfiVersion(root),
            ffiChecksum = FfiChecksum(root),
            coreCrate = "stampeded",
            coreVersion,
            coreChecksum = checksum.Groups[1].Value,
            coreSource = Registry,
            cargoLockSha256 = HashFile(lockfile),
            inputsSha256 = HashInputs(root),
            headerSha256 = HashFile(Path.Combine(destination, "stampeded.h")),
            licenseSha256 = HashFile(Path.Combine(destination, "LICENSE")),
            schemaSha256 = HashFile(Path.Combine(destination, "schema.sql")),
            migrationsSha256 = HashMigrations(stagedMigrations)
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Verify(root);
    }

    public static void Verify(string root)
    {
        var directory = Path.Combine(root, PreparedDirectory);
        var provenance = Path.Combine(directory, "upstream.json");
        if (!File.Exists(provenance))
            throw new InvalidDataException("Published FFI sources are not prepared. Run: bash scripts/prepare-native.sh");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(provenance));
        var record = manifest.RootElement;
        if (ReadProperty(record, "ffiVersion") != FfiVersion(root) ||
            ReadProperty(record, "ffiChecksum") != FfiChecksum(root) ||
            HashFile(Path.Combine(directory, $"stampeded-ffi-{FfiVersion(root)}.crate")) != FfiChecksum(root) ||
            ReadProperty(record, "cargoLockSha256") != HashFile(Path.Combine(root, "native/Cargo.lock")) ||
            ReadProperty(record, "inputsSha256") != HashInputs(root) ||
            ReadProperty(record, "headerSha256") != HashFile(Path.Combine(directory, "stampeded.h")) ||
            ReadProperty(record, "licenseSha256") != HashFile(Path.Combine(directory, "LICENSE")) ||
            ReadProperty(record, "schemaSha256") != HashFile(Path.Combine(directory, "schema.sql")) ||
            ReadProperty(record, "migrationsSha256") != HashMigrations(Path.Combine(directory, "migrations")))
            throw new InvalidDataException("Published FFI inputs or staged files changed. Run: bash scripts/prepare-native.sh");
        Console.WriteLine($"Verified published stampeded-ffi {ReadProperty(record, "ffiVersion")} and stampeded {ReadProperty(record, "coreVersion")}.");
    }

    private static string ReadConfig(string root, string name)
    {
        using var config = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "native/upstream.json")));
        return ReadProperty(config.RootElement, name);
    }

    private static string HashInputs(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in new[] { "native/upstream.json", "native/Cargo.lock", "native/LICENSE" })
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file + "\0"));
            hash.AppendData(File.ReadAllBytes(Path.Combine(root, file)));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string HashMigrations(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = Directory.GetFiles(directory, "*.sql").Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidDataException("No staged migrations were found.");
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file) + "\0"));
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
        if (!manifest.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidDataException($"Expected a nonempty '{name}' string.");
        return property.GetString()!;
    }
}
