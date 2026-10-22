namespace Stampeded;

/// <summary>A handler is resolved in a fresh dependency injection scope for each attempt.</summary>
public interface IJobHandler<in TArguments>
{
    Task ExecuteAsync(TArguments arguments, JobContext context, CancellationToken cancellationToken);
}
