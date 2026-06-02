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
/// End-to-end slice tests for the payment + invoice coordination path.
/// Mocked at the repo and provider boundary; real service objects wired together.
/// </summary>
public class PaymentIntegrationTests
{
    // ── repos ──────────────────────────────────────────────────────────────────
    private readonly Mock<IPaymentRepository>  _paymentRepo  = new();
    private readonly Mock<IInvoiceRepository>  _invoiceRepo  = new();
    private readonly Mock<IOrderRepository>    _orderRepo    = new();
    private readonly Mock<IAuditRepository>    _auditRepo    = new();

    // ── external ───────────────────────────────────────────────────────────────
    private readonly Mock<IPaymentProvider>        _provider        = new();
    private readonly Mock<IPaymentProviderFactory> _providerFactory = new();
    private readonly Mock<INotificationService>    _notify          = new();
    private readonly Mock<IStorageService>         _storage         = new();

    // ── loggers ────────────────────────────────────────────────────────────────
    private readonly Mock<ILogger<PaymentService>> _paymentLogger = new();
    private readonly Mock<ILogger<InvoiceService>> _invoiceLogger = new();
    private readonly Mock<ILogger<AuditService>>   _auditLogger   = new();

    public PaymentIntegrationTests()
    {
        _providerFactory
            .Setup(f => f.GetProvider(It.IsAny<PaymentProvider>()))
            .Returns(_provider.Object);

        _auditRepo
            .Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default))
            .Returns(Task.CompletedTask);
    }

    private AuditService BuildAuditService() =>
        new(_auditRepo.Object, _auditLogger.Object);

    private InvoiceService BuildInvoiceService() => new(
        _invoiceRepo.Object, _orderRepo.Object, BuildAuditService(),
        _notify.Object, _storage.Object, _invoiceLogger.Object);

    private PaymentService BuildPaymentService() => new(
        _paymentRepo.Object, BuildInvoiceService(), _notify.Object,
        BuildAuditService(), _providerFactory.Object, _paymentLogger.Object);

    // ── helpers ────────────────────────────────────────────────────────────────

    private void SetupProviderSuccess(string providerPaymentId = "pi_test_001")
    {
        _provider
            .Setup(p => p.ChargeAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync(new PaymentProviderResult(
                Success:           true,
                ProviderPaymentId: providerPaymentId,
                ProviderChargeId:  "ch_test_001",
                CardLast4:         "4242",
                CardBrand:         "Visa"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Happy path: charge succeeds and invoice is marked paid
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChargeAndApply_FullFlow_InvoiceMarkedPaid()
    {
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // invoice starts at Issued, balance 200
        var invoice = new InvoiceBuilder()
            .WithId(invoiceId)
            .WithStatus(InvoiceStatus.Issued)
            .WithAmount(200m)
            .Build();

        _invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId, default)).ReturnsAsync(invoice);
        _invoiceRepo.Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), default))
            .ReturnsAsync((Invoice inv, CancellationToken _) => inv);
        _invoiceRepo.Setup(r => r.AddPaymentAsync(It.IsAny<InvoicePayment>(), default))
            .Returns(Task.CompletedTask);

        _paymentRepo.Setup(r => r.CreateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => { p.Id = paymentId; return p; });
        _paymentRepo.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);

        SetupProviderSuccess();

        var result = await BuildPaymentService()
            .ChargeAsync(1, 200m, "USD", invoiceId, PaymentProvider.Stripe, null);

        Assert.Equal(PaymentStatus.Succeeded, result.Status);

        _invoiceRepo.Verify(
            r => r.AddPaymentAsync(
                It.Is<InvoicePayment>(ip => ip.InvoiceId == invoiceId && ip.Amount == 200m),
                default),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Provider decline: payment stored as Failed, invoice untouched
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChargeAsync_ProviderDecline_InvoicePaymentNotRecorded()
    {
        var invoiceId = Guid.NewGuid();

        _paymentRepo.Setup(r => r.CreateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);
        _paymentRepo.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);

        _provider
            .Setup(p => p.ChargeAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync(new PaymentProviderResult(
                Success:      false,
                ErrorCode:    "insufficient_funds",
                ErrorMessage: "Your card has insufficient funds."));

        var result = await BuildPaymentService()
            .ChargeAsync(1, 150m, "USD", invoiceId, PaymentProvider.Stripe, null);

        Assert.Equal(PaymentStatus.Failed, result.Status);

        // invoice must NOT receive a payment record on decline
        _invoiceRepo.Verify(
            r => r.AddPaymentAsync(It.IsAny<InvoicePayment>(), default),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Full refund: payment status flips to Refunded
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RefundAsync_FullAmount_SetsRefundedStatus()
    {
        var payment = new PaymentBuilder()
            .WithAmount(300m)
            .WithStatus(PaymentStatus.Succeeded)
            .Build();
        payment.RefundedAmount = 0m;
        payment.ProviderPaymentId = "pi_abc";

        _paymentRepo.Setup(r => r.GetByIdAsync(payment.Id, default)).ReturnsAsync(payment);
        _paymentRepo.Setup(r => r.AddRefundAsync(It.IsAny<PaymentRefund>(), default))
            .Returns(Task.CompletedTask);
        _paymentRepo.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);

        _provider
            .Setup(p => p.RefundAsync(It.IsAny<Payment>(), 300m, default))
            .ReturnsAsync(new PaymentProviderResult(
                Success:          true,
                ProviderRefundId: "re_full_001"));

        var result = await BuildPaymentService()
            .RefundAsync(payment.Id, 300m, "customer request", "admin");

        Assert.Equal(PaymentStatus.Refunded, result.Status);
        Assert.Equal(300m, result.RefundedAmount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Partial refund: payment remains Succeeded with updated RefundedAmount
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RefundAsync_PartialAmount_StatusRemainsSucceeded()
    {
        var payment = new PaymentBuilder()
            .WithAmount(400m)
            .WithStatus(PaymentStatus.Succeeded)
            .Build();
        payment.RefundedAmount = 0m;

        _paymentRepo.Setup(r => r.GetByIdAsync(payment.Id, default)).ReturnsAsync(payment);
        _paymentRepo.Setup(r => r.AddRefundAsync(It.IsAny<PaymentRefund>(), default))
            .Returns(Task.CompletedTask);
        _paymentRepo.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);

        _provider
            .Setup(p => p.RefundAsync(It.IsAny<Payment>(), 100m, default))
            .ReturnsAsync(new PaymentProviderResult(
                Success:          true,
                ProviderRefundId: "re_partial_001"));

        var result = await BuildPaymentService()
            .RefundAsync(payment.Id, 100m, "partial return", "admin");

        Assert.Equal(PaymentStatus.Succeeded, result.Status);
        Assert.Equal(100m, result.RefundedAmount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Provider network error: payment saved as Failed and exception surfaced
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChargeAsync_NetworkError_PaymentStoredAsFailed()
    {
        _paymentRepo.Setup(r => r.CreateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);
        _paymentRepo.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);

        _provider
            .Setup(p => p.ChargeAsync(It.IsAny<Payment>(), default))
            .ThrowsAsync(new HttpRequestException("Connection timed out"));

        await Assert.ThrowsAsync<PaymentFailedException>(
            () => BuildPaymentService().ChargeAsync(1, 99m, "USD", null, PaymentProvider.Stripe, null));

        _paymentRepo.Verify(
            r => r.UpdateAsync(It.Is<Payment>(p => p.Status == PaymentStatus.Failed), default),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Notification sent on charge success
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChargeAsync_Success_SendsPaymentConfirmationNotification()
    {
        _paymentRepo.Setup(r => r.CreateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);
        _paymentRepo.Setup(r => r.UpdateAsync(It.IsAny<Payment>(), default))
            .ReturnsAsync((Payment p, CancellationToken _) => p);

        SetupProviderSuccess("pi_notify_test");

        await BuildPaymentService()
            .ChargeAsync(3, 250m, "USD", null, PaymentProvider.Stripe, null);

        _notify.Verify(
            n => n.SendAsync(
                It.Is<NotificationRequest>(r => r.Type == NotificationType.PaymentConfirmation),
                default),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Idempotency: same idempotency key returns existing payment without
    // re-charging the provider (important for retry logic in API clients)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChargeAsync_WithIdempotencyKey_DoesNotDoubleCharge()
    {
        var idempotencyKey = "idem_key_abc123";
        var existingPayment = new PaymentBuilder()
            .WithStatus(PaymentStatus.Succeeded)
            .WithIdempotencyKey(idempotencyKey)
            .Build();

        _paymentRepo
            .Setup(r => r.GetByIdempotencyKeyAsync(idempotencyKey, default))
            .ReturnsAsync(existingPayment);

        var result = await BuildPaymentService()
            .ChargeAsync(1, 100m, "USD", null, PaymentProvider.Stripe, idempotencyKey);

        Assert.Equal(PaymentStatus.Succeeded, result.Status);
        Assert.Equal(idempotencyKey, result.IdempotencyKey);

        // provider must NOT be called — we returned the cached result
        _provider.Verify(
            p => p.ChargeAsync(It.IsAny<Payment>(), default),
            Times.Never);
    }
}
