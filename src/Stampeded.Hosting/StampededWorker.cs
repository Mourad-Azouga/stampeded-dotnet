using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stampeded.Hosting;

internal sealed class StampededWorker : BackgroundService
{
    private readonly StampededClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StampededWorker> _logger;
    private readonly StampededWorkerOptions _options;
    private readonly IReadOnlyDictionary<string, HandlerRegistration> _handlers;

    public StampededWorker(StampededClient client, IServiceScopeFactory scopeFactory,
        IEnumerable<HandlerRegistration> handlers, IOptions<StampededWorkerOptions> options,
        ILogger<StampededWorker> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _options.Validate();
        _handlers = handlers.ToDictionary(handler => handler.TaskName, StringComparer.Ordinal);
        if (_handlers.Count == 0)
            throw new InvalidOperationException("Register at least one Stampeded handler before starting a worker.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var puller = _client.StartPuller(_options.ToPullerOptions());
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        // The ABI blocks. Fixed dedicated threads wait for leases and async handlers without
        // occupying ThreadPool threads for each native poll. Rust owns its own Tokio runtime.
        var slots = Enumerable.Range(0, _options.MaxConcurrency).Select(_ => Task.Factory.StartNew(() =>
        {
            try
            {
                RunSlot(puller, stop.Token);
            }
            catch
            {
                stop.Cancel();
                throw;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        await Task.WhenAll(slots).ConfigureAwait(false);
    }

    private void RunSlot(StampededPuller puller, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var lease = puller.Next(TimeSpan.FromMilliseconds(250));
            if (lease is null)
                continue;
            // Don't begin prefetched work after shutdown was requested.
            if (stoppingToken.IsCancellationRequested)
            {
                lease.Requeue();
                break;
            }
            ExecuteJobAsync(lease, stoppingToken).GetAwaiter().GetResult();
        }
    }

    private async Task ExecuteJobAsync(JobLease lease, CancellationToken stoppingToken)
    {
        using var timeout = new CancellationTokenSource(_options.JobTimeout);
        using var drain = new CancellationTokenSource();
        using var shutdown = stoppingToken.Register(() => drain.CancelAfter(_options.ShutdownTimeout));
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, drain.Token);
        try
        {
            try
            {
                if (!_handlers.TryGetValue(lease.Job.TaskName, out var registration))
                    throw new DiscardJobException($"No handler is registered for '{lease.Job.TaskName}'.");
                await using var scope = _scopeFactory.CreateAsyncScope();
                await registration.ExecuteAsync(scope.ServiceProvider, lease.Job, lease.Context,
                    _client.JsonOptions, execution.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (execution.IsCancellationRequested)
            {
                if (timeout.IsCancellationRequested)
                    lease.TimeOut("The C# handler exceeded its configured time limit.");
                else
                    lease.Requeue();
                return;
            }
            catch (JobOwnershipLostException)
            {
                _logger.LogWarning("Ownership of job {JobId} was lost", lease.Job.Id);
                return;
            }
            catch (DiscardJobException exception)
            {
                lease.Discard(Report(exception));
                return;
            }
            catch (RetryJobException exception)
            {
                lease.Fail(Report(exception), exception.RetryAfter);
                return;
            }
            catch (JsonException exception)
            {
                lease.Discard(Report(exception));
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Handler failed for job {JobId}, attempt {Attempt}", lease.Job.Id, lease.Job.Attempt);
                lease.Fail(Report(exception));
                return;
            }

            if (timeout.IsCancellationRequested)
                lease.TimeOut("The C# handler completed after its configured time limit.");
            else
                lease.Complete();
        }
        catch (StampededException exception)
        {
            // Reporting can fail after the database committed. Never repeat the handler here.
            _logger.LogError(exception, "Could not report an outcome for job {JobId}; upstream recovery must resolve it", lease.Job.Id);
        }
    }

    private static string Report(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}".Replace('\0', '\uFFFD');
}
