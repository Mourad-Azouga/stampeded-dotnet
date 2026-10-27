using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stampeded.Hosting;
using UglyToad.PdfPig;

namespace Stampeded.Tests;

public sealed class WorkloadTests
{
    [WorkloadFact]
    public Task GeneratesAPdfAndDeliversItToTheLocalInbox() => RunAsync(retryAfterRendering: false);

    [WorkloadFact]
    public Task RetryResumesAfterThePdfStepWithoutRenderingItAgain() => RunAsync(retryAfterRendering: true);

    private static async Task RunAsync(bool retryAfterRendering)
    {
        await using var database = await Database.CreateAsync();
        using var workload = new InvoiceWorkload(retryAfterRendering);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddStampeded(database.Url);
        builder.Services.AddSingleton(workload);
        builder.Services.AddStampededHandler<Invoice, InvoiceHandler>("invoice.deliver");
        builder.Services.AddStampededWorker(options =>
        {
            options.Queues = ["mail"];
            options.MaxConcurrency = 1;
            options.PollInterval = TimeSpan.FromMilliseconds(20);
            options.ShutdownTimeout = TimeSpan.FromSeconds(1);
        });
        using var host = builder.Build();
        var invoice = new Invoice(42, $"customer-{Guid.NewGuid():N}@example.test", 123.45m);
        var id = database.Client.Enqueue(new EnqueueRequest<Invoice>
        {
            Queue = "mail",
            TaskName = "invoice.deliver",
            Arguments = invoice,
            Options = new() { MaxAttempts = 2 }
        }).JobId!.Value;
        await host.StartAsync();
        try
        {
            var row = await database.WaitForAsync(id, "completed");
            Assert.Equal(retryAfterRendering ? 2 : 1, row.GetProperty("attempt").GetInt32());
        }
        finally { await host.StopAsync(); }

        Assert.Equal(1, workload.RenderCount);
        using var message = await workload.ReadCapturedMessageAsync(id);
        Assert.Equal(invoice.Recipient, Assert.Single(message.To.Mailboxes).Address);
        Assert.Contains("مرحبا résumé", message.TextBody);
        var attachment = Assert.IsType<MimeKit.MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("application/pdf", attachment.ContentType.MimeType);
        Assert.NotNull(attachment.Content);
        using var content = new MemoryStream();
        await attachment.Content.DecodeToAsync(content);
        Assert.Equal(await File.ReadAllBytesAsync(workload.PdfPath(id, invoice.Number)), content.ToArray());
        using var pdf = PdfDocument.Open(content.ToArray());
        Assert.Equal(1, pdf.NumberOfPages);
        Assert.Contains("Invoice 42", pdf.GetPage(1).Text);
        Assert.Contains("123.45", pdf.GetPage(1).Text);
    }

    public sealed record Invoice(int Number, string Recipient, decimal Total);

    public sealed class InvoiceHandler(InvoiceWorkload workload) : IJobHandler<Invoice>
    {
        public async Task ExecuteAsync(Invoice arguments, JobContext context, CancellationToken cancellationToken)
        {
            var path = await context.StepAsync("render_pdf",
                token => workload.RenderAsync(context.Job.Id, arguments, token), cancellationToken);
            workload.FailAfterRenderingIfRequested();
            await context.StepAsync("send_email", async token =>
            {
                await workload.SendAsync(context.Job.Id, arguments, path, token);
                return "captured";
            }, cancellationToken);
        }
    }
}

public sealed class WorkloadFactAttribute : FactAttribute
{
    public WorkloadFactAttribute([CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
    {
        if ((string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STAMPEDED_TEST_CONNECTION_STRING")) ||
             string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STAMPEDED_TEST_MAILPIT_URL"))) &&
            Environment.GetEnvironmentVariable("STAMPEDED_REQUIRE_WORKLOAD_TESTS") != "1")
            Skip = "Set the PostgreSQL and local Mailpit settings to run email/PDF workloads.";
    }
}
