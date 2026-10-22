using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stampeded;
using Stampeded.Hosting;

var databaseUrl = Environment.GetEnvironmentVariable("STAMPEDED_DATABASE_URL")
    ?? throw new InvalidOperationException("Set STAMPEDED_DATABASE_URL to a PostgreSQL 18 URL with the upstream schema installed.");

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddStampeded(databaseUrl);
builder.Services.AddStampededHandler<WelcomeEmail, WelcomeEmailHandler>("email.welcome");
builder.Services.AddStampededWorker(options =>
{
    options.Queues = ["mail"];
    options.MaxConcurrency = 4;
});
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(40));

using var host = builder.Build();
var client = host.Services.GetRequiredService<StampededClient>();
// Provision the mail queue using the upstream daemon/SQL before running this sample.
var result = client.Enqueue(new EnqueueRequest<WelcomeEmail>
{
    Queue = "mail",
    TaskName = "email.welcome",
    Arguments = new(UserId: 42),
    Options = new() { UniqueKey = "welcome:42", MaxAttempts = 5 }
});
Console.WriteLine(result.Enqueued ? $"Queued job {result.JobId}" : "A welcome job for this user already waits.");
await host.RunAsync();

public sealed record WelcomeEmail(int UserId);

public sealed class WelcomeEmailHandler(ILogger<WelcomeEmailHandler> logger) : IJobHandler<WelcomeEmail>
{
    public async Task ExecuteAsync(WelcomeEmail arguments, JobContext context, CancellationToken cancellationToken)
    {
        var message = await context.StepAsync("prepare", async token =>
        {
            await Task.Delay(100, token);
            return $"Welcome, user {arguments.UserId}";
        }, cancellationToken);

        // This sample prepares text only. A later integration will call a real email provider.
        logger.LogInformation("{Message}; job {JobId}, attempt {Attempt}", message, context.Job.Id, context.Job.Attempt);
    }
}
