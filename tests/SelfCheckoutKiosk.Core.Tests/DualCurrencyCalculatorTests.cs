using SelfCheckoutKiosk.Core.Currency;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class DualCurrencyCalculatorTests
{
    private readonly DualCurrencyCalculator _calculator = new();

    [Fact]
    public void CalculateChange_ExactTender_ReturnsEmptyBreakdown()
    {
        ChangeBreakdownAssertEmpty(_calculator.CalculateChange(totalUsd: 10m, tenderedUsd: 10m, usdToKhrRate: 4100m));
    }

    [Fact]
    public void CalculateChange_UnderTender_ReturnsEmptyBreakdown()
    {
        ChangeBreakdownAssertEmpty(_calculator.CalculateChange(totalUsd: 10m, tenderedUsd: 5m, usdToKhrRate: 4100m));
    }

    [Fact]
    public void CalculateChange_WholeDollarOverpayment_ReturnsOnlyUsdNotes()
    {
        var change = _calculator.CalculateChange(totalUsd: 10m, tenderedUsd: 35m, usdToKhrRate: 4100m);

        // $25 change: 1x$20 + 1x$5
        Assert.Equal(2, change.UsdNotes.Count);
        Assert.Contains(change.UsdNotes, kv => kv.Key.Amount == 20m && kv.Value == 1);
        Assert.Contains(change.UsdNotes, kv => kv.Key.Amount == 5m && kv.Value == 1);
        Assert.Empty(change.KhrNotes);
        Assert.Equal(0m, change.DiscardedKhrRemainder);
    }

    [Fact]
    public void CalculateChange_SubDollarRemainder_ConvertsToKhrRoundedDown()
    {
        // Total 9.50, tendered 10.00 -> 0.50 USD change -> 0.50 * 4100 = 2050 KHR
        // -> rounds down to 2000 (largest dispensable <= 2050 given denominations
        // 100000/50000/20000/10000/5000/1000/500/100): 2000 = 2x1000.
        var change = _calculator.CalculateChange(totalUsd: 9.5m, tenderedUsd: 10m, usdToKhrRate: 4100m);

        Assert.Empty(change.UsdNotes);
        Assert.NotEmpty(change.KhrNotes);
        long totalKhrDispensed = 0;
        foreach (var kv in change.KhrNotes)
            totalKhrDispensed += (long)kv.Key.Amount * kv.Value;

        Assert.Equal(2000L, totalKhrDispensed);
        Assert.Equal(50m, change.DiscardedKhrRemainder);
    }

    [Fact]
    public void CalculateChange_KhrRemainderBelowSmallestNote_DiscardsEntireRemainder()
    {
        // 0.01 USD overpayment * 4100 = 41 KHR, below the 100 KHR smallest note.
        var change = _calculator.CalculateChange(totalUsd: 9.99m, tenderedUsd: 10m, usdToKhrRate: 4100m);

        Assert.Empty(change.UsdNotes);
        Assert.Empty(change.KhrNotes);
        Assert.Equal(41m, change.DiscardedKhrRemainder);
    }

    [Fact]
    public void CalculateChange_Exact100KhrStep_Dispenses100KhrNote()
    {
        // 0.52 USD overpayment * 4000 = 2080 KHR -> rounds down to 2000 KHR (discarded 80 KHR)
        // 0.525 USD overpayment * 4000 = 2100 KHR -> exactly 2100 KHR: 1x2000 + 1x100 KHR.
        var change = _calculator.CalculateChange(totalUsd: 9.475m, tenderedUsd: 10m, usdToKhrRate: 4000m);

        Assert.Empty(change.UsdNotes);
        Assert.NotEmpty(change.KhrNotes);
        long totalKhrDispensed = 0;
        foreach (var kv in change.KhrNotes)
            totalKhrDispensed += (long)kv.Key.Amount * kv.Value;

        Assert.Equal(2100L, totalKhrDispensed);
        Assert.Equal(0m, change.DiscardedKhrRemainder);
        Assert.Contains(change.KhrNotes, kv => kv.Key.Amount == 100m && kv.Value == 1);
    }

    [Fact]
    public void CalculateChange_LargeOverpayment_SplitsUsdAcrossDenominations()
    {
        var change = _calculator.CalculateChange(totalUsd: 1m, tenderedUsd: 100m, usdToKhrRate: 4100m);

        long totalUsdDispensed = 0;
        foreach (var kv in change.UsdNotes)
            totalUsdDispensed += (long)kv.Key.Amount * kv.Value;

        Assert.Equal(99L, totalUsdDispensed);
        Assert.Empty(change.KhrNotes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CalculateChange_InvalidRate_ThrowsInvalidExchangeRateException(decimal rate)
    {
        var ex = Assert.Throws<InvalidExchangeRateException>(
            () => _calculator.CalculateChange(totalUsd: 1m, tenderedUsd: 5m, usdToKhrRate: rate));
        Assert.Equal(rate, ex.UsdToKhrRate);
    }

    [Fact]
    public void CalculateChange_NegativeTotal_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => _calculator.CalculateChange(totalUsd: -1m, tenderedUsd: 5m, usdToKhrRate: 4100m));

    [Fact]
    public void CalculateChange_NegativeTendered_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => _calculator.CalculateChange(totalUsd: 1m, tenderedUsd: -5m, usdToKhrRate: 4100m));

    private static void ChangeBreakdownAssertEmpty(SelfCheckoutKiosk.Domain.ValueObjects.ChangeBreakdown change)
    {
        Assert.Empty(change.UsdNotes);
        Assert.Empty(change.KhrNotes);
        Assert.Equal(0m, change.DiscardedKhrRemainder);
    }
}
