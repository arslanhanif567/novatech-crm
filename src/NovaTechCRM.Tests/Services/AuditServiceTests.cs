using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services;

namespace NovaTechCRM.Tests.Services;

public class AuditServiceTests
{
    private readonly Mock<IAuditRepository>      _repo   = new();
    private readonly Mock<ILogger<AuditService>> _logger = new();

    private AuditService CreateSut() => new(_repo.Object, _logger.Object);

    [Fact]
    public async Task LogAsync_BatchesEntries_BeforeFlushThreshold()
    {
        var sut = CreateSut();

        // log 99 entries (threshold is 100 — should not flush yet)
        for (var i = 0; i < 99; i++)
            await sut.LogAsync(AuditAction.Created, "Order", i.ToString(), null);

        _repo.Verify(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default),
            Times.Never,
            "Batch should not flush until threshold is reached");
    }

    [Fact]
    public async Task LogAsync_FlushesAutomatically_AtThreshold()
    {
        _repo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        for (var i = 0; i < 100; i++)
            await sut.LogAsync(AuditAction.Created, "Order", i.ToString(), null);

        _repo.Verify(r => r.BulkInsertAsync(
            It.Is<IEnumerable<AuditLog>>(l => l.Count() == 100), default),
            Times.Once);
    }

    [Fact]
    public async Task FlushBatchAsync_WritesRemainingEntries()
    {
        _repo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        await sut.LogAsync(AuditAction.Updated, "Customer", "42", "user-1");
        await sut.LogAsync(AuditAction.Deleted, "Invoice", "inv-7", "user-2");

        await sut.FlushBatchAsync();

        _repo.Verify(r => r.BulkInsertAsync(
            It.Is<IEnumerable<AuditLog>>(l => l.Count() == 2), default),
            Times.Once);
    }

    [Fact]
    public async Task FlushBatchAsync_IsNoOp_WhenBatchIsEmpty()
    {
        var sut = CreateSut();
        await sut.FlushBatchAsync();

        _repo.Verify(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default),
            Times.Never);
    }

    [Fact]
    public async Task LogAsync_SerializesOldAndNewValues()
    {
        _repo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        await sut.LogAsync(
            AuditAction.Updated, "Payment", "pay-1", "admin",
            oldValues: new { Status = "Processing" },
            newValues: new { Status = "Succeeded" });

        await sut.FlushBatchAsync();

        _repo.Verify(r => r.BulkInsertAsync(
            It.Is<IEnumerable<AuditLog>>(logs =>
                logs.First().OldValuesJson!.Contains("Processing") &&
                logs.First().NewValuesJson!.Contains("Succeeded")),
            default), Times.Once);
    }

    [Fact]
    public async Task FlushBatchAsync_RequeuesEntries_OnRepoFailure()
    {
        var callCount = 0;
        _repo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default))
            .ThrowsAsync(new Exception("DB down"));

        var sut = CreateSut();

        await sut.LogAsync(AuditAction.Created, "X", "1", null);

        // first flush fails
        await Assert.ThrowsAsync<Exception>(() => sut.FlushBatchAsync());

        // entry re-queued — second flush should attempt again
        _repo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<AuditLog>>(), default))
            .Returns(Task.CompletedTask);

        await sut.FlushBatchAsync();

        _repo.Verify(r => r.BulkInsertAsync(
            It.Is<IEnumerable<AuditLog>>(l => l.Any()),
            default), Times.Exactly(2));
    }

    [Fact]
    public async Task GetEntityHistoryAsync_DelegatesToRepository()
    {
        var expected = new List<AuditLog>
        {
            new() { EntityType = "Invoice", EntityId = "inv-1", Action = AuditAction.Created }
        };

        _repo.Setup(r => r.GetByEntityAsync("Invoice", "inv-1", 50, default))
            .ReturnsAsync(expected);

        var sut    = CreateSut();
        var result = await sut.GetEntityHistoryAsync("Invoice", "inv-1");

        Assert.Single(result);
        Assert.Equal("Invoice", result[0].EntityType);
    }
}
