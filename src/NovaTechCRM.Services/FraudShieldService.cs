using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services;

/// <summary>
/// Stub implementation of the FraudShield external service.
/// Real implementation would call the FraudShield REST API.
/// </summary>
public class FraudShieldService : IFraudShieldService
{
    private readonly ILogger<FraudShieldService> _logger;

    public FraudShieldService(ILogger<FraudShieldService> logger)
    {
        _logger = logger;
    }

    public async Task<FraudCheckResult> CheckAsync(Order order, CancellationToken ct = default)
    {
        // Simulates variable latency of the external FraudShield API (200ms–800ms).
        // This latency window is what causes the race condition in OrderService.
        var latency = Random.Shared.Next(200, 800);
        await Task.Delay(latency, ct);

        var passed = order.TotalAmount < 5000m;
        var riskLevel = order.TotalAmount switch
        {
            < 500m  => FraudRiskLevel.Low,
            < 2000m => FraudRiskLevel.Medium,
            < 5000m => FraudRiskLevel.High,
            _       => FraudRiskLevel.Critical
        };

        var result = new FraudCheckResult
        {
            CheckId = Guid.NewGuid().ToString(),
            Passed = passed,
            RiskLevel = riskLevel,
            Reason = passed ? "Automated check passed" : "Amount exceeds threshold"
        };

        _logger.LogInformation("FraudShield check {CheckId} completed in {Latency}ms: passed={Passed}",
            result.CheckId, latency, result.Passed);

        return result;
    }
}
