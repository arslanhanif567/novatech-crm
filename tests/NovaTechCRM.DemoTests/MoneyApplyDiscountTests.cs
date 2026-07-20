using Xunit;
using NovaTechCRM.Domain.ValueObjects;

namespace NovaTechCRM.DemoTests;

/// <summary>
/// NOVA-DEMO — "Discounts produce negative order totals".
///
/// Money.ApplyDiscount receives a whole-number percentage (20 == 20%), so it must
/// convert it to a fraction (divide by 100) before applying it. The bug used the
/// percent directly: (1 - 20) = -19, turning a $100 charge into -$1900.
///
/// These tests FAIL on the buggy code and PASS once `/ 100` is restored.
/// </summary>
public class MoneyApplyDiscountTests
{
    [Fact]
    public void ApplyDiscount_TwentyPercentOnHundred_ReturnsEighty()
    {
        var price = new Money(100m, "USD");

        var discounted = price.ApplyDiscount(20m);

        Assert.Equal(80m, discounted.Amount);
    }

    [Theory]
    [InlineData(0, 100)]    // no discount
    [InlineData(10, 90)]
    [InlineData(25, 75)]
    [InlineData(50, 50)]
    [InlineData(100, 0)]    // full discount → free, never negative
    public void ApplyDiscount_ValidPercent_ProducesExpectedNonNegativeTotal(decimal percent, decimal expected)
    {
        var result = new Money(100m, "USD").ApplyDiscount(percent);

        Assert.Equal(expected, result.Amount);
        Assert.False(result.IsNegative, $"A {percent}% discount must never produce a negative total.");
    }

    [Fact]
    public void ApplyDiscount_PreservesCurrency()
    {
        var result = new Money(100m, "EUR").ApplyDiscount(10m);

        Assert.Equal("EUR", result.Currency);
    }
}
