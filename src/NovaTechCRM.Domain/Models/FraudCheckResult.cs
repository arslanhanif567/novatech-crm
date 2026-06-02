namespace NovaTechCRM.Domain.Models;

public enum FraudRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public class FraudCheckResult
{
    public string CheckId { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public FraudRiskLevel RiskLevel { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
