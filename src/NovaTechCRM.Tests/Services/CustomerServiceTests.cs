using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services;
using NovaTechCRM.Services.Interfaces;
using NovaTechCRM.Tests.Builders;

namespace NovaTechCRM.Tests.Services;

public class CustomerServiceTests
{
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<IOrderRepository>    _orderRepo    = new();
    private readonly Mock<IAuditService>       _audit        = new();
    private readonly Mock<ICacheService>       _cache        = new();
    private readonly Mock<ILogger<CustomerService>> _logger  = new();

    private CustomerService CreateSut() => new(
        _customerRepo.Object,
        _orderRepo.Object,
        _audit.Object,
        _cache.Object,
        _logger.Object);

    [Fact]
    public async Task GetByIdAsync_ReturnsCustomer_WhenExists()
    {
        var customer = new CustomerBuilder().WithId(42).Build();
        _customerRepo
            .Setup(r => r.GetByIdAsync(42, default))
            .ReturnsAsync(customer);

        var result = await CreateSut().GetByIdAsync(42);

        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        _customerRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), default))
            .ReturnsAsync((Customer?)null);

        var result = await CreateSut().GetByIdAsync(99);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_SetsDefaultTierToStandard()
    {
        var customer = new CustomerBuilder().Build();
        _customerRepo
            .Setup(r => r.CreateAsync(It.IsAny<Customer>(), default))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var result = await CreateSut().CreateAsync(customer);

        Assert.Equal(CustomerTier.Standard, result.Tier);
    }

    [Fact]
    public async Task RecalculateTierAsync_PromotesToGold_WhenMonthlySpendOver5000()
    {
        var customer = new CustomerBuilder()
            .WithId(1)
            .WithTier(CustomerTier.Standard)
            .WithMonthlySpend(6000m)
            .Build();

        _customerRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(customer);
        _customerRepo.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), default))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var tier = await CreateSut().RecalculateTierAsync(1);

        Assert.Equal(CustomerTier.Gold, tier);
    }

    [Fact]
    public async Task RecalculateTierAsync_PromotesToPlatinum_WhenMonthlySpendOver20000()
    {
        var customer = new CustomerBuilder()
            .WithId(1)
            .WithMonthlySpend(25000m)
            .Build();

        _customerRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(customer);
        _customerRepo.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), default))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var tier = await CreateSut().RecalculateTierAsync(1);

        Assert.Equal(CustomerTier.Platinum, tier);
    }

    [Fact]
    public async Task RecalculateTierAsync_Throws_WhenCustomerNotFound()
    {
        _customerRepo.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((Customer?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateSut().RecalculateTierAsync(999));
    }

    // NOVA-52: This test documents the N+1 query problem in GetAllWithStatsAsync.
    // When loading a page of 25 customers, the service fires 25 additional repo calls
    // (one GetOrderSummariesAsync per customer). The test passes today but exposes
    // the performance issue — each call should be visible in query logs.
    [Fact]
    public async Task GetAllWithStatsAsync_MakesNPlusOneCallsToOrderRepo()
    {
        var customers = Enumerable.Range(1, 10)
            .Select(i => new CustomerBuilder().WithId(i).Build())
            .ToList();

        _customerRepo
            .Setup(r => r.GetAllAsync(1, 10, null, null, null, default))
            .ReturnsAsync((customers.AsReadOnly() as IReadOnlyList<Customer>, 10));

        _orderRepo
            .Setup(r => r.GetByCustomerAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new List<Order>());

        await CreateSut().GetAllWithStatsAsync(1, 10);

        // NOVA-52: verify the N+1 — each of 10 customers triggers a separate order query
        _orderRepo.Verify(
            r => r.GetByCustomerAsync(It.IsAny<string>(), default),
            Times.Exactly(10),
            "NOVA-52: N+1 confirmed — each customer triggers an individual order query");
    }

    [Fact]
    public async Task UpdateAsync_AuditsChange()
    {
        var customer = new CustomerBuilder().WithId(5).Build();
        _customerRepo.Setup(r => r.GetByIdAsync(5, default)).ReturnsAsync(customer);
        _customerRepo.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), default))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        await CreateSut().UpdateAsync(customer);

        _audit.Verify(a => a.LogAsync(
            AuditAction.Updated, "Customer", "5",
            It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<object?>(),
            default), Times.Once);
    }

    [Theory]
    [InlineData(0,    CustomerTier.Standard)]
    [InlineData(999,  CustomerTier.Standard)]
    [InlineData(1000, CustomerTier.Silver)]
    [InlineData(5000, CustomerTier.Gold)]
    [InlineData(20000,CustomerTier.Platinum)]
    public async Task RecalculateTierAsync_CorrectlyMapsSpendToTier(
        decimal monthlySpend, CustomerTier expectedTier)
    {
        var customer = new CustomerBuilder().WithId(1).WithMonthlySpend(monthlySpend).Build();
        _customerRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(customer);
        _customerRepo.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), default))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var result = await CreateSut().RecalculateTierAsync(1);

        Assert.Equal(expectedTier, result);
    }
}
