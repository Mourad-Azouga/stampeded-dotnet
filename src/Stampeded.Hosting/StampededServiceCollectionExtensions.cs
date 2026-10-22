using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Stampeded.Hosting;

public static class StampededServiceCollectionExtensions
{
    /// <summary>Register a DI-owned Rust client. Connecting happens when the client is first resolved.</summary>
    public static IServiceCollection AddStampeded(this IServiceCollection services, string databaseUrl,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        Interop.NativeCall.ValidateString(databaseUrl, nameof(databaseUrl));
        if (services.Any(descriptor => descriptor.ServiceType == typeof(StampededClient)))
            throw new InvalidOperationException("Stampeded is already registered. Configure one database per service collection.");
        services.AddSingleton(_ => StampededClient.Connect(databaseUrl, serializerOptions));
        services.AddLogging();
        return services;
    }

    public static IServiceCollection AddStampededHandler<TArguments, THandler>(
        this IServiceCollection services, string taskName)
        where THandler : class, IJobHandler<TArguments>
    {
        ArgumentNullException.ThrowIfNull(services);
        Interop.NativeCall.ValidateString(taskName, nameof(taskName));
        services.TryAddScoped<THandler>();
        services.AddSingleton(new HandlerRegistration(taskName, async (provider, job, context, json, token) =>
        {
            var arguments = job.Arguments.Deserialize<TArguments>(json)
                ?? throw new DiscardJobException($"Task '{taskName}' arguments deserialize to null.");
            await provider.GetRequiredService<THandler>().ExecuteAsync(arguments, context, token).ConfigureAwait(false);
        }));
        return services;
    }

    public static IServiceCollection AddStampededWorker(this IServiceCollection services,
        Action<StampededWorkerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<StampededWorkerOptions>();
        if (configure is not null)
            options.Configure(configure);
        options.Validate(value => { value.Validate(); return true; }).ValidateOnStart();
        services.AddHostedService<StampededWorker>();
        return services;
    }
}

internal sealed record HandlerRegistration(string TaskName,
    Func<IServiceProvider, Job, JobContext, JsonSerializerOptions, CancellationToken, Task> ExecuteAsync);
