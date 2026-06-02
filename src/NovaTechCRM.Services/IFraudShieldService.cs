using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services;

public interface IFraudShieldService
{
    Task<FraudCheckResult> CheckAsync(Order order, CancellationToken ct = default);
}
