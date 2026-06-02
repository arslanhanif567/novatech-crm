using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services.Interfaces;

public interface IInventoryService
{
    Task<Inventory?> GetBySkuAsync(string sku, string? warehouseId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Inventory>> GetLowStockAsync(CancellationToken ct = default);
    Task<bool> IsAvailableAsync(string sku, int quantity, CancellationToken ct = default);
    Task ReserveStockAsync(string sku, int quantity, Guid orderId, CancellationToken ct = default);
    Task ReleaseReservationAsync(Guid orderId, CancellationToken ct = default);
    Task CommitReservationAsync(Guid orderId, CancellationToken ct = default);
    Task AdjustAsync(string sku, int delta, string reason, string userId, CancellationToken ct = default);
    Task<IReadOnlyList<InventoryTransaction>> GetTransactionsAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyList<StockAlert>> GetActiveAlertsAsync(CancellationToken ct = default);
}
