using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stampeded;

internal static class JobJson
{
    // Shared argument names are snake_case so C#, Ruby, Go and Python can agree on the payload.
    internal static JsonSerializerOptions DefaultOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    internal static string SerializeObject<T>(T value, JsonSerializerOptions options)
    {
        var element = JsonSerializer.SerializeToElement(value, options);
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Job arguments must serialize to a JSON object.", nameof(value));

        var json = element.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > 1_048_576)
            throw new ArgumentException("Job arguments cannot exceed 1 MiB. Pass a reference to large data.", nameof(value));
        return json;
    }

    private static readonly JsonSerializerOptions WireOptions = CreateWireOptions();

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions(DefaultOptions);
        options.Converters.Add(new JsonStringEnumConverter<JobState>(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    internal static Job ParseJob(string json) => JsonSerializer.Deserialize<Job>(json, WireOptions)
        ?? throw new InvalidOperationException("The native library returned an empty job.");

    internal static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
