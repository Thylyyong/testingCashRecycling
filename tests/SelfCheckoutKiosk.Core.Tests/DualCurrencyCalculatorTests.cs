using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

/// <summary>
/// Unit tests for <see cref="DualCurrencyCalculator"/> (Blueprint §3).
///
/// AI NOTE — before editing this file:
///   1. Re-read Blueprint §3 (dual-currency change algorithm).
///   2. Rate is hardcoded: 1 USD = 4 100 KHR. No live API.
///   3. Sub-dollar KHR change is ALWAYS floored to nearest 100 KHR.
/// </summary>
public sealed class DualCurrencyCalculatorTests
{
    private readonly DualCurrencyCalculator _calc = new();

    // -----------------------------------------------------------------------
    // ToUsdEquivalent
    // -----------------------------------------------------------------------

    [Fact]
    public void ToUsdEquivalent_UsdNote_ReturnsFaceValue()
    {
        var usd = DualCurrencyCalculator.ToUsdEquivalent(Money.Usd(5.00m));

        Assert.Equal(5.00m, usd);
    }

    [Fact]
    public void ToUsdEquivalent_KhrNote_DividesByRate()
    {
        // 4 100 KHR ÷ 4 100 = 1.00 USD
        var usd = DualCurrencyCalculator.ToUsdEquivalent(Money.Khr(4_100m));

        Assert.Equal(1.00m, usd);
    }

    [Fact]
    public void ToUsdEquivalent_HalfDollarInKhr_ConvertsCorrectly()
    {
        // 2 050 KHR ÷ 4 100 = 0.50 USD
        var usd = DualCurrencyCalculator.ToUsdEquivalent(Money.Khr(2_050m));

        Assert.Equal(0.50m, usd);
    }

    // -----------------------------------------------------------------------
    // CalculateChange — exact payment
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateChange_ExactPayment_ReturnsEmptyBreakdown()
    {
        // Customer pays exactly $2.00 for a $2.00 product
        var change = _calc.CalculateChange(totalUsd: 2.00m, tenderedUsd: 2.00m);

        Assert.Empty(change.UsdNotes);
        Assert.Empty(change.KhrNotes);
    }

    // -----------------------------------------------------------------------
    // CalculateChange — whole-dollar USD change
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateChange_WholeDollarOverpayment_ReturnsUsdNotesOnly()
    {
        // Pays $5 for a $2 product → $3 change in USD
        var change = _calc.CalculateChange(totalUsd: 2.00m, tenderedUsd: 5.00m);

        var totalUsdChange = change.UsdNotes.Sum(kv => kv.Key.Amount * kv.Value);
        Assert.Equal(3.00m, totalUsdChange);
        Assert.Empty(change.KhrNotes);
    }

    [Fact]
    public void CalculateChange_UsdNotes_UsesGreedyLargestFirst()
    {
        // $21 change → 1x $20 + 1x $1
        var change = _calc.CalculateChange(totalUsd: 0.00m, tenderedUsd: 21.00m);

        Assert.Contains(change.UsdNotes, kv => kv.Key.Amount == 20m && kv.Value == 1);
        Assert.Contains(change.UsdNotes, kv => kv.Key.Amount == 1m  && kv.Value == 1);
    }

    // -----------------------------------------------------------------------
    // CalculateChange — sub-dollar KHR change
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateChange_HalfDollarRemainder_ReturnsKhrChange()
    {
        // $1.50 product, $2 tendered → $0.50 overpayment
        // 0.50 × 4100 = 2050 KHR → floored to 2000 KHR
        var change = _calc.CalculateChange(totalUsd: 1.50m, tenderedUsd: 2.00m);

        Assert.Empty(change.UsdNotes);
        var totalKhr = change.KhrNotes.Sum(kv => kv.Key.Amount * kv.Value);
        Assert.Equal(2_000m, totalKhr);
    }

    [Fact]
    public void CalculateChange_KhrChange_FloorsToNearest100()
    {
        // 0.499 × 4100 = 2045.9 → floored to 2000 KHR (nearest 100)
        var change = _calc.CalculateChange(totalUsd: 1.501m, tenderedUsd: 2.00m);

        Assert.Empty(change.UsdNotes);
        var totalKhr = change.KhrNotes.Sum(kv => kv.Key.Amount * kv.Value);
        // 0.499 * 4100 = 2045.9 → (int)2045 → floor to 2000
        Assert.Equal(2_000m, totalKhr);
    }

    [Fact]
    public void CalculateChange_SubCentRemainder_ReturnsNoKhrChange()
    {
        // 0.001 × 4100 = 4.1 KHR → below 100 KHR minimum → no KHR change
        var change = _calc.CalculateChange(totalUsd: 1.999m, tenderedUsd: 2.00m);

        Assert.Empty(change.UsdNotes);
        Assert.Empty(change.KhrNotes);
    }

    // -----------------------------------------------------------------------
    // CalculateChange — mixed USD + KHR change
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateChange_MixedChange_ReturnsBothUsdAndKhr()
    {
        // $3.50 product, $5 tendered → $1.50 change
        // $1 USD + 0.50 × 4100 = 2050 KHR → 2000 KHR
        var change = _calc.CalculateChange(totalUsd: 3.50m, tenderedUsd: 5.00m);

        var totalUsd = change.UsdNotes.Sum(kv => kv.Key.Amount * kv.Value);
        var totalKhr = change.KhrNotes.Sum(kv => kv.Key.Amount * kv.Value);

        Assert.Equal(1.00m, totalUsd);
        Assert.Equal(2_000m, totalKhr);
    }

    // -----------------------------------------------------------------------
    // CalculateChange — guard
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateChange_UnderpaymentThrows()
    {
        Assert.Throws<ArgumentException>(() =>
            _calc.CalculateChange(totalUsd: 5.00m, tenderedUsd: 3.00m));
    }

    // -----------------------------------------------------------------------
    // Rate constant
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultRate_Is4100()
    {
        Assert.Equal(4_100m, DualCurrencyCalculator.DefaultUsdToKhrRate);
    }
}
