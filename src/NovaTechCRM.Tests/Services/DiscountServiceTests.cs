using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services;
using NovaTechCRM.Services.Interfaces;
using NovaTechCRM.Tests.Builders;

namespace NovaTechCRM.Tests.Services;

public class DiscountServiceTests
{
    private readonly Mock<IDiscountRepository> _repo   = new();
    private readonly Mock<ICacheService>       _cache  = new();
    private readonly Mock<ILogger<DiscountService>> _logger = new();

    private DiscountService CreateSut() => new(_repo.Object, _cache.Object, _logger.Object);

    [Fact]
    public async Task ApplyToOrderAsync_AppliesPercentageDiscount()
    {
        var discount = new DiscountBuilder()
            .WithCode("SAVE10")
            .WithType(DiscountType.Percentage)
            .WithAmount(10m)
            .Stackable(false)
            .Build();

        _repo.Setup(r => r.GetByCodeAsync("SAVE10", default)).ReturnsAsync(discount);
        _repo.Setup(r => r.IncrementUsageAsync(discount.Id, default)).ReturnsAsync(true);

        var order = Orders.Simple();
        order.TotalAmount = 200m;

        var result = await CreateSut().ApplyCodeAsync(order, "SAVE10");

        Assert.True(result.WasApplied);
        Assert.Equal(20m, result.DiscountAmount);   // 10% of 200
        Assert.Equal(180m, result.FinalAmount);
    }

    [Fact]
    public async Task ApplyToOrderAsync_AppliesFixedDiscount()
    {
        var discount = new DiscountBuilder()
            .WithType(DiscountType.Fixed)
            .WithAmount(50m)
            .Stackable(false)
            .Build();

        _repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync(discount);
        _repo.Setup(r => r.IncrementUsageAsync(discount.Id, default)).ReturnsAsync(true);

        var order = Orders.Simple();
        order.TotalAmount = 300m;

        var result = await CreateSut().ApplyCodeAsync(order, "FIXED50");

        Assert.True(result.WasApplied);
        Assert.Equal(50m, result.DiscountAmount);
        Assert.Equal(250m, result.FinalAmount);
    }

    [Fact]
    public async Task ApplyToOrderAsync_RejectsExpiredCode()
    {
        _repo.Setup(r => r.GetByCodeAsync("EXPIRED", default))
            .ReturnsAsync((Discount?)null);

        var result = await CreateSut().ApplyCodeAsync(Orders.Simple(), "EXPIRED");

        Assert.False(result.WasApplied);
        Assert.Equal("Code not found or expired.", result.Reason);
    }

    [Fact]
    public async Task ApplyToOrderAsync_RejectsWhenBelowMinimumOrder()
    {
        var discount = new DiscountBuilder()
            .WithType(DiscountType.Percentage)
            .WithAmount(15m)
            .Build();

        discount.MinOrderAmount = 500m;

        _repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync(discount);

        var order = Orders.Simple();
        order.TotalAmount = 100m;    // below minimum

        var result = await CreateSut().ApplyCodeAsync(order, "BIG15");

        Assert.False(result.WasApplied);
        Assert.Contains("500", result.Reason);
    }

    // NOVA-58: This test documents the stackable discount conflict resolution bug.
    // Two stackable discounts both with ConflictResolution = BestOnly should result
    // in only the HIGHER discount being applied. Instead, both are applied additively,
    // overcharging the discount by the amount of the smaller one.
    [Fact]
    public async Task CalculateStack_NOVA58_StackableDiscountsIgnoreConflictResolution()
    {
        var d1 = new DiscountBuilder()
            .WithCode("A")
            .WithType(DiscountType.Percentage)
            .WithAmount(20m)
            .WithPriority(2)
            .WithConflict(DiscountConflictResolution.BestOnly)
            .Stackable(true)
            .Build();

        var d2 = new DiscountBuilder()
            .WithCode("B")
            .WithType(DiscountType.Percentage)
            .WithAmount(10m)
            .WithPriority(1)
            .WithConflict(DiscountConflictResolution.BestOnly)
            .Stackable(true)
            .Build();

        _repo.Setup(r => r.GetActiveAsync(default)).ReturnsAsync(new List<Discount> { d1, d2 });

        var order = Orders.Simple();
        order.TotalAmount = 100m;

        var results = await CreateSut().CalculateStackAsync(order);
        var totalDiscount = results.Where(r => r.WasApplied).Sum(r => r.DiscountAmount);

        // BUG (NOVA-58): ConflictResolution.BestOnly is ignored.
        // Should apply only the best (20% = 20), but applies both (20 + 10*(100-20) = 28 or 30).
        // This assertion documents the current broken behavior — it passes, proving the bug exists.
        Assert.True(totalDiscount > 20m,
            "NOVA-58: Both stackable discounts applied even though ConflictResolution = BestOnly. " +
            $"Expected ≤ 20 (best only), got {totalDiscount}.");
    }

    [Fact]
    public async Task CalculateStack_CapsTotalAtMaxDiscountAmount()
    {
        var discount = new DiscountBuilder()
            .WithType(DiscountType.Percentage)
            .WithAmount(50m)
            .Stackable(true)
            .Build();

        discount.MaxDiscountAmount = 30m;

        _repo.Setup(r => r.GetActiveAsync(default)).ReturnsAsync(new List<Discount> { discount });

        var order = Orders.Simple();
        order.TotalAmount = 200m;   // 50% = 100, but cap is 30

        var results = await CreateSut().CalculateStackAsync(order);
        var applied = results.First(r => r.WasApplied);

        Assert.Equal(30m, applied.DiscountAmount);
    }

    [Fact]
    public async Task ValidateCodeAsync_ReturnsFalse_WhenUsageLimitReached()
    {
        var discount = new DiscountBuilder().Build();
        discount.UsageLimit = 100;
        discount.UsageCount = 100;

        _repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync(discount);

        var valid = await CreateSut().ValidateCodeAsync("FULL", Orders.Simple());

        Assert.False(valid.IsValid);
    }
}
