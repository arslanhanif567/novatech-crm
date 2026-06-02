using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Exceptions;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Tests.Services;

public class InventoryServiceTests
{
    private readonly Mock<IInventoryRepository>      _repo   = new();
    private readonly Mock<ILogger<InventoryService>> _logger = new();

    private InventoryService CreateSut() => new(_repo.Object, _logger.Object);

    private static Inventory StockedItem(int available = 10, int reserved = 0) => new()
    {
        Id                = Guid.NewGuid(),
        ProductId         = Guid.NewGuid(),
        WarehouseId       = "WH-US-EAST",
        QuantityAvailable = available,
        QuantityReserved  = reserved,
        QuantityOnHand    = available + reserved,
        IsDiscontinued    = false,
    };

    [Fact]
    public async Task ReserveAsync_SucceedsWhenStockAvailable()
    {
        var inv = StockedItem(available: 10);
        _repo.Setup(r => r.GetByProductAsync(inv.ProductId, null, null, default))
            .ReturnsAsync(inv);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Inventory>(), default))
            .ReturnsAsync((Inventory i, CancellationToken _) => i);
        _repo.Setup(r => r.CreateReservationAsync(It.IsAny<InventoryReservation>(), default))
            .ReturnsAsync((InventoryReservation r, CancellationToken _) => r);

        var result = await CreateSut().ReserveAsync(inv.ProductId, null, "WH-US-EAST", 3, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal(3, result.Quantity);
    }

    [Fact]
    public async Task ReserveAsync_Throws_WhenInsufficientStock()
    {
        var inv = StockedItem(available: 2);
        _repo.Setup(r => r.GetByProductAsync(inv.ProductId, null, null, default))
            .ReturnsAsync(inv);

        await Assert.ThrowsAsync<InsufficientInventoryException>(
            () => CreateSut().ReserveAsync(inv.ProductId, null, "WH-US-EAST", 5, Guid.NewGuid()));
    }

    [Fact]
    public async Task ReserveAsync_Throws_WhenInventoryNotFound()
    {
        _repo.Setup(r => r.GetByProductAsync(It.IsAny<Guid>(), null, null, default))
            .ReturnsAsync((Inventory?)null);

        await Assert.ThrowsAsync<InsufficientInventoryException>(
            () => CreateSut().ReserveAsync(Guid.NewGuid(), null, "WH", 1, Guid.NewGuid()));
    }

    // NOVA-61: Race condition test — two concurrent reservations against the same stock.
    // Both threads read QuantityAvailable = 5 and both see sufficient stock.
    // Both proceed to write, resulting in QuantityReserved being incremented twice
    // even though combined demand (8) exceeds supply (5).
    //
    // This test demonstrates the window by running two Tasks concurrently against
    // the same mocked inventory object (no locking in InventoryService.ReserveAsync).
    [Fact]
    public async Task ReserveAsync_NOVA61_ConcurrentReservationsCanOverCommitStock()
    {
        var inv = StockedItem(available: 5);
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();

        // Both reads return the same snapshot — simulates the race window
        _repo.Setup(r => r.GetByProductAsync(inv.ProductId, null, null, default))
            .ReturnsAsync(inv);

        var callCount = 0;
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Inventory>(), default))
            .ReturnsAsync((Inventory i, CancellationToken _) =>
            {
                // simulate concurrent writes landing on same stale base
                callCount++;
                return i;
            });
        _repo.Setup(r => r.CreateReservationAsync(It.IsAny<InventoryReservation>(), default))
            .ReturnsAsync((InventoryReservation r, CancellationToken _) => r);

        // fire both reservations simultaneously
        var t1 = CreateSut().ReserveAsync(inv.ProductId, null, "WH-US-EAST", 4, orderId1);
        var t2 = CreateSut().ReserveAsync(inv.ProductId, null, "WH-US-EAST", 4, orderId2);

        // BUG (NOVA-61): both should not succeed — combined demand 8 > stock 5.
        // Without locking, both reads see available = 5, both pass the check, both write.
        // The test below documents that both tasks complete without exception (the bug).
        await Task.WhenAll(t1, t2);     // <-- this should throw on one, but doesn't

        Assert.Equal(2, callCount);     // both writes executed — stock over-committed
    }

    [Fact]
    public async Task ReleaseReservationAsync_RestoresQuantity()
    {
        var reservationId = Guid.NewGuid();
        var inv = StockedItem(available: 7, reserved: 3);

        var reservation = new InventoryReservation
        {
            Id          = reservationId,
            InventoryId = inv.Id,
            ProductId   = inv.ProductId,
            Quantity    = 3,
            ExpiresAt   = DateTime.UtcNow.AddMinutes(10),
        };

        _repo.Setup(r => r.GetReservationAsync(reservationId, default)).ReturnsAsync(reservation);
        _repo.Setup(r => r.GetByIdAsync(inv.Id, default)).ReturnsAsync(inv);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Inventory>(), default))
            .ReturnsAsync((Inventory i, CancellationToken _) => i);
        _repo.Setup(r => r.DeleteReservationAsync(reservationId, default)).Returns(Task.CompletedTask);

        await CreateSut().ReleaseReservationAsync(reservationId);

        _repo.Verify(r => r.UpdateAsync(
            It.Is<Inventory>(i => i.QuantityReserved == 0 && i.QuantityAvailable == 10),
            default), Times.Once);
    }

    [Fact]
    public async Task GetLowStockAsync_ReturnsItemsBelowThreshold()
    {
        var items = new List<Inventory>
        {
            StockedItem(available: 2),
            StockedItem(available: 8),
        };

        _repo.Setup(r => r.GetLowStockAsync(5, default))
            .ReturnsAsync(new List<Inventory> { items[0] });

        var result = await CreateSut().GetLowStockAsync(5);

        Assert.Single(result);
        Assert.Equal(2, result[0].QuantityAvailable);
    }
}
