using System.Runtime.CompilerServices;
using System.Text.Json;
using Npgsql;

namespace Stampeded.Tests;

// Database access is deliberately confined to this test harness. Production code goes through Rust.
internal sealed class Database : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly string _schema;
    public NpgsqlDataSource DataSource { get; }
    public string Url { get; }
    public StampededClient Client { get; private set; } = null!;

    private Database(string connectionString, string schema)
    {
        _connectionString = connectionString;
        _schema = schema;
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, MaxPoolSize = 5 };
        DataSource = NpgsqlDataSource.Create(settings.ConnectionString);
        Url = new UriBuilder("postgres", settings.Host!, settings.Port, settings.Database)
        {
            UserName = settings.Username,
            Password = settings.Password,
            Query = $"options={Uri.EscapeDataString($"-c search_path={schema}")}&sslmode=disable"
        }.Uri.AbsoluteUri;
    }

    public static async Task<Database> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("STAMPEDED_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set STAMPEDED_TEST_CONNECTION_STRING to a disposable PostgreSQL 18 database.");
        var database = new Database(connectionString, $"bindings_{Guid.NewGuid():N}");
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {database._schema}", connection);
            await create.ExecuteNonQueryAsync();
            await database.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql")));
            await database.ExecuteAsync("SELECT stampeded_create_queue('mail')");
            database.Client = StampededClient.Connect(database.Url);
            return database;
        }
        catch { await database.DisposeAsync(); throw; }
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<JsonElement> ReadAsync(Guid id)
    {
        await using var command = DataSource.CreateCommand("SELECT row_to_json(j)::text FROM stampeded_jobs j WHERE id = $1");
        command.Parameters.AddWithValue(id);
        return JobJson.Parse((string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"No job found for {id}.")));
    }

    public async Task<JsonElement> WaitForAsync(Guid id, string state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        JsonElement last = default;
        try
        {
            while (true)
            {
                last = await ReadAsync(id);
                if (last.GetProperty("state").GetString() == state)
                    return last;
                await Task.Delay(20, timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Job {id} did not reach {state}. Last row: {last}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await DataSource.DisposeAsync();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {_schema} CASCADE", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute([CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STAMPEDED_TEST_CONNECTION_STRING")) &&
            Environment.GetEnvironmentVariable("STAMPEDED_REQUIRE_DATABASE_TESTS") != "1")
            Skip = "Set STAMPEDED_TEST_CONNECTION_STRING to run PostgreSQL 18 integration tests.";
    }
}
