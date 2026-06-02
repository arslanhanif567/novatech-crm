using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services;

public interface IDiscountEngine
{
    decimal Apply(decimal originalPrice, IEnumerable<DiscountRule> rules);
}

public class DiscountEngine : IDiscountEngine
{
    public decimal Apply(decimal originalPrice, IEnumerable<DiscountRule> rules)
    {
        var topRule = rules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Category)
            .FirstOrDefault();

        if (topRule is null)
            return originalPrice;

        var price = originalPrice - originalPrice * (topRule.DiscountPercent / 100m);
        return Math.Max(price, 0m);
    }
}
