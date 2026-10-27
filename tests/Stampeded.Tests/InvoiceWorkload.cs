using System.Globalization;
using System.Text.Json;
using MailKit.Security;
using MimeKit;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Invoice = Stampeded.Tests.WorkloadTests.Invoice;

namespace Stampeded.Tests;

/// <summary>A disposable, real PDF/SMTP workload used only by the integration tests.</summary>
public sealed class InvoiceWorkload : IDisposable
{
    private readonly string _directory;
    private readonly bool _retain;
    private readonly HttpClient _inbox;
    private int _pendingFailure;
    private int _renders;
    public int RenderCount => Volatile.Read(ref _renders);

    public InvoiceWorkload(bool retryAfterRendering)
    {
        var artifacts = Environment.GetEnvironmentVariable("STAMPEDED_TEST_ARTIFACTS");
        _retain = !string.IsNullOrWhiteSpace(artifacts);
        _directory = Path.Combine(_retain ? artifacts! : Path.GetTempPath(), $"invoice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _pendingFailure = retryAfterRendering ? 1 : 0;
        _inbox = new()
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("STAMPEDED_TEST_MAILPIT_URL")
                ?? throw new InvalidOperationException("Set STAMPEDED_TEST_MAILPIT_URL to a local Mailpit inbox.")),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public string PdfPath(Guid id, int number) => Path.Combine(_directory, $"invoice-{number}-{id:N}.pdf");

    public async Task<string> RenderAsync(Guid id, Invoice invoice, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _renders);
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText($"Invoice {invoice.Number}", 18, new PdfPoint(40, 780), font);
        page.AddText($"Total: {invoice.Total.ToString("0.00", CultureInfo.InvariantCulture)}", 12, new PdfPoint(40, 750), font);
        var path = PdfPath(id, invoice.Number);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, builder.Build(), cancellationToken);
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public void FailAfterRenderingIfRequested()
    {
        if (Interlocked.Exchange(ref _pendingFailure, 0) != 0)
            throw new RetryJobException(TimeSpan.Zero, "Injected failure after the PDF checkpoint, before SMTP delivery.");
    }

    public async Task SendAsync(Guid id, Invoice invoice, string path, CancellationToken cancellationToken)
    {
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Stampeded tests", "stampeded@example.test"));
        message.To.Add(MailboxAddress.Parse(invoice.Recipient));
        message.MessageId = $"{id:N}@stampeded.test";
        message.Subject = $"Invoice {invoice.Number} [{id:N}]";
        var body = new BodyBuilder { TextBody = $"مرحبا résumé — your invoice {invoice.Number} is attached." };
        body.Attachments.Add(path, new ContentType("application", "pdf"));
        message.Body = body.ToMessageBody();
        using var smtp = new MailKit.Net.Smtp.SmtpClient();
        var hostname = Environment.GetEnvironmentVariable("STAMPEDED_TEST_SMTP_HOST") ?? "localhost";
        var port = int.Parse(Environment.GetEnvironmentVariable("STAMPEDED_TEST_SMTP_PORT") ?? "1025", CultureInfo.InvariantCulture);
        await smtp.ConnectAsync(hostname, port, SecureSocketOptions.None, cancellationToken);
        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }

    public async Task<MimeMessage> ReadCapturedMessageAsync(Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var content = await _inbox.GetByteArrayAsync("/api/v1/messages?limit=100", timeout.Token);
            using var listing = JsonDocument.Parse(content);
            var matches = listing.RootElement.GetProperty("messages").EnumerateArray()
                .Where(message => message.GetProperty("Subject").GetString()!.Contains(id.ToString("N"), StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 0)
            {
                var message = Assert.Single(matches);
                var messageId = Uri.EscapeDataString(message.GetProperty("ID").GetString()!);
                var raw = await _inbox.GetByteArrayAsync($"/api/v1/message/{messageId}/raw", timeout.Token);
                await File.WriteAllBytesAsync(Path.Combine(_directory, "message.eml"), raw, timeout.Token);
                using var stream = new MemoryStream(raw);
                return await MimeMessage.LoadAsync(stream, timeout.Token);
            }
            await Task.Delay(50, timeout.Token);
        }
    }

    public void Dispose()
    {
        _inbox.Dispose();
        if (!_retain && Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
