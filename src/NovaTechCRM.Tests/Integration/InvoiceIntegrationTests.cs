using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services;
using NovaTechCRM.Services.Interfaces;
using NovaTechCRM.Tests.Builders;

namespace NovaTechCRM.Tests.Integration;

/// <summary>
/// End-to-end slice tests: InvoiceService wired to real service dependencies
/// (mocked at the repo boundary). These exercise multi-service coordination
/// that unit tests with full isolation can't capture.
/// </summary>
public class InvoiceIntegrationTests
{
    // ── repos ──────────────────────────────────────────────────────────────────
    private readonly Mock<IInvoiceRepository>    _invoiceRepo = new();
    private readonly Mock<IOrderRepository>      _orderRepo   = new();
    private readonly Mock<IPaymentRepository>    _paymentRepo = new();
    private readonly Mock<IAuditRepository>      _auditRepo   = new();

    // ── services ──────────────────────────────────────────────────────────────
    private readonly Mock<INotificationService>  _notify  = new();
    private readonly Mock<IStorageService>       _storage = new();

    private readonly Mock<ILogger<InvoiceService>> _invoiceLogger = new();
    private readonly Mock<ILogger<AuditService>>   _auditLogger   = new();

    private AuditService   BuildAuditService()   => new(_auditRepo.Object,  _auditLogger.Object);
    private InvoiceService BuildInvoiceService() => new(
        _invoiceRepo.Object, _orderRepo.Object, BuildAuditService(),
        _notify.Object, _storage.Object, _invoiceLogger.Object);

    // ── helpers ───────────────────────────────────────────────────────────────

    private void SetupInvoiceCreate()
    {
        _invoiceRepo
            .Setup(r => r.CreateAsync(It.IsAny<Invoice>(), default))
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);
    }

    private void SetupInvoiceUpdate()
    {
        _invoiceRepo
            .Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), default))
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);
    }

    private void SetupAuditFlush()
    {
        _auditRepo
            .Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default))
            .Returns(Task.CompletedTask);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Full lifecycle: draft → issue → partial pay → fully paid
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullLifecycle_DraftToFullyPaid()
    {
        SetupInvoiceCreate();
        SetupInvoiceUpdate();
        SetupAuditFlush();

        var sut = BuildInvoiceService();

        // 1. Create draft
        var invoice = await sut.CreateDraftAsync(
            customerId: 42,
            items: new List<InvoiceLineItem>
            {
                new() { Description = "Consulting", UnitPrice = 500m, Quantity = 2 },
                new() { Description = "Expenses",   UnitPrice = 150m, Quantity = 1 },
            },
            dueDate: DateTime.UtcNow.AddDays(30),
            "user-1");

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(1150m, invoice.TotalAmount);

        // 2. Issue
        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, default)).ReturnsAsync(invoice);
        var issued = await sut.IssueAsync(invoice.Id, "user-1");
        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        Assert.NotNull(issued.IssuedAt);

        // 3. Partial payment
        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, default)).ReturnsAsync(issued);
        _invoiceRepo.Setup(r => r.AddPaymentAsync(It.IsAny<InvoicePayment>(), default))
            .Returns(Task.CompletedTask);

        var afterPartial = await sut.RecordPaymentAsync(invoice.Id, 500m, Guid.NewGuid(), default);
        Assert.Equal(InvoiceStatus.PartiallyPaid, afterPartial.Status);
        Assert.Equal(500m, afterPartial.AmountPaid);
        Assert.Equal(650m, afterPartial.BalanceDue);

        // 4. Final payment
        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, default)).ReturnsAsync(afterPartial);
        var fullyPaid = await sut.RecordPaymentAsync(invoice.Id, 650m, Guid.NewGuid(), default);
        Assert.Equal(InvoiceStatus.Paid, fullyPaid.Status);
        Assert.Equal(0m, fullyPaid.BalanceDue);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Void prevents further payments
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RecordPaymentAsync_Throws_WhenInvoiceIsVoided()
    {
        var invoice = new InvoiceBuilder()
            .WithStatus(InvoiceStatus.Void)
            .WithAmount(200m)
            .Build();

        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, default)).ReturnsAsync(invoice);
        SetupAuditFlush();

        var sut = BuildInvoiceService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RecordPaymentAsync(invoice.Id, 100m, Guid.NewGuid(), default));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Audit trail: every status transition emits an audit log
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IssueAsync_EmitsAuditEntry()
    {
        SetupInvoiceCreate();
        SetupInvoiceUpdate();
        SetupAuditFlush();

        var sut = BuildInvoiceService();

        var invoice = await sut.CreateDraftAsync(
            42,
            new List<InvoiceLineItem> { new() { Description = "Item", UnitPrice = 100m, Quantity = 1 } },
            DateTime.UtcNow.AddDays(30),
            "admin");

        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, default)).ReturnsAsync(invoice);
        await sut.IssueAsync(invoice.Id, "admin");
        await BuildAuditService().FlushBatchAsync();

        _auditRepo.Verify(
            r => r.BulkInsertAsync(
                It.Is<IEnumerable<AuditLog>>(logs =>
                    logs.Any(l => l.EntityType == "Invoice" && l.Action == AuditAction.Updated)),
                default),
            Times.AtLeastOnce);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Overdue detection
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetOverdueAsync_ReturnsOnlyPastDueIssuedInvoices()
    {
        var overdue = new List<Invoice>
        {
            new InvoiceBuilder()
                .WithStatus(InvoiceStatus.Issued)
                .WithDueDate(DateTime.UtcNow.AddDays(-3))
                .Build(),
        };

        _invoiceRepo.Setup(r => r.GetOverdueAsync(default)).ReturnsAsync(overdue);

        var result = await BuildInvoiceService().GetOverdueAsync();

        Assert.Single(result);
        Assert.Equal(InvoiceStatus.Issued, result[0].Status);
        Assert.True(result[0].DueDate < DateTime.UtcNow);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PDF generation is triggered on issue
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IssueAsync_GeneratesAndStoresPdf()
    {
        SetupInvoiceCreate();
        SetupInvoiceUpdate();
        SetupAuditFlush();

        _storage
            .Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), "application/pdf", default))
            .ReturnsAsync("https://storage.example.com/invoices/test.pdf");

        var sut = BuildInvoiceService();

        var invoice = await sut.CreateDraftAsync(
            42,
            new List<InvoiceLineItem> { new() { Description = "Service", UnitPrice = 300m, Quantity = 1 } },
            DateTime.UtcNow.AddDays(15),
            "user-1");

        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, default)).ReturnsAsync(invoice);

        var issued = await sut.IssueAsync(invoice.Id, "user-1");

        Assert.NotNull(issued.PdfUrl);
        Assert.Contains("storage.example.com", issued.PdfUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Send triggers notification
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendAsync_TriggersEmailNotification()
    {
        var invoice = new InvoiceBuilder()
            .WithStatus(InvoiceStatus.Issued)
            .WithCustomerId(5)
            .Build();

        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, default)).ReturnsAsync(invoice);
        SetupInvoiceUpdate();
        SetupAuditFlush();

        _notify
            .Setup(n => n.SendAsync(
                It.Is<NotificationRequest>(r => r.Channel == NotificationChannel.Email),
                default))
            .Returns(Task.CompletedTask);

        await BuildInvoiceService().SendAsync(invoice.Id, "user-1");

        _notify.Verify(
            n => n.SendAsync(
                It.Is<NotificationRequest>(r => r.Channel == NotificationChannel.Email),
                default),
            Times.Once);
    }
}
