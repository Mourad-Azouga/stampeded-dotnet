using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Stampeded.Tests;

public sealed class SchemaCompatibilityTests
{
    [PostgresFact]
    public async Task SchemaThreeKeepsHourlyAndDailyWindowsDistinct()
    {
        await using var database = await Database.CreateAsync();
        await using var version = database.DataSource.CreateCommand("SELECT version FROM stampeded_schema");
        Assert.Equal(3, (int)(await version.ExecuteScalarAsync())!);
        await database.ExecuteAsync("""
            INSERT INTO stampeded_jobs (queue, task, dedup_scope, dedup_period, inserted_at)
            VALUES ('mail', 'report', 'account:42', 'hour', '2026-10-24T00:30:00Z'),
                   ('mail', 'report', 'account:42', 'day', '2026-10-24T00:30:00Z')
            """);
        CompleteBoth(database.Client);
    }

    [PostgresFact]
    public async Task APeriodlessScopeCannotForgeAPeriodicJobsKey()
    {
        await using var database = await Database.CreateAsync();
        await database.ExecuteAsync("""
            INSERT INTO stampeded_jobs (queue, task, dedup_scope, dedup_period, inserted_at)
            VALUES ('mail', 'report', 'a', 'hour', '2025-01-01T00:00:00Z')
            """);
        var result = database.Client.Enqueue(new EnqueueRequest<object>
        {
            Queue = "mail",
            TaskName = "report",
            Arguments = new { account_id = 42 },
            Options = new() { UniqueKey = "a:hour:1735689600" }
        });
        Assert.True(result.Enqueued);
        CompleteBoth(database.Client);
    }

    [TlsFact]
    public async Task NativeConnectionsVerifyThePrivateCaAndRejectAnUntrustedCa()
    {
        await using var database = await Database.CreateAsync();
        var rootCertificate = Path.GetFullPath(Environment.GetEnvironmentVariable("STAMPEDED_TEST_TLS_ROOT_CERT")!);
        var name = $"stampeded_tls_{Guid.NewGuid():N}";
        string SecureUrl(string certificate) => database.Url.Replace("sslmode=disable",
            $"sslmode=verify-full&sslrootcert={Uri.EscapeDataString(certificate)}", StringComparison.Ordinal)
            + $"&application_name={name}";

        using var client = StampededClient.Connect(SecureUrl(rootCertificate));
        Assert.True(client.Enqueue(new EnqueueRequest<object>
        {
            Queue = "mail",
            TaskName = "tls.test",
            Arguments = new { secured = true }
        }).Enqueued);
        await using var connections = database.DataSource.CreateCommand("""
            SELECT count(*) FROM pg_stat_ssl s JOIN pg_stat_activity a USING (pid)
            WHERE a.application_name = $1 AND s.ssl
            """);
        connections.Parameters.AddWithValue(name);
        Assert.True((long)(await connections.ExecuteScalarAsync())! > 0);

        var wrongCertificate = Path.Combine(Path.GetTempPath(), $"stampeded-untrusted-{Guid.NewGuid():N}.pem");
        try
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Untrusted test root", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            await File.WriteAllTextAsync(wrongCertificate, certificate.ExportCertificatePem());
            Assert.Throws<StampededException>(() => StampededClient.Connect(SecureUrl(wrongCertificate)));
        }
        finally { File.Delete(wrongCertificate); }
    }

    private static void CompleteBoth(StampededClient client)
    {
        using var puller = client.StartPuller(new() { Queues = ["mail"], MaxLeases = 2, BatchSize = 2 });
        using var first = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        using var second = Assert.IsType<JobLease>(puller.Next(TimeSpan.FromSeconds(3)));
        Assert.NotEqual(first.Job.Id, second.Job.Id);
        first.Complete();
        second.Complete();
    }
}

public sealed class TlsFactAttribute : FactAttribute
{
    public TlsFactAttribute([CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
    {
        if ((string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STAMPEDED_TEST_CONNECTION_STRING")) ||
             string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STAMPEDED_TEST_TLS_ROOT_CERT"))) &&
            Environment.GetEnvironmentVariable("STAMPEDED_REQUIRE_TLS_TESTS") != "1")
            Skip = "Set the PostgreSQL connection and TLS root certificate to test native TLS.";
    }
}
