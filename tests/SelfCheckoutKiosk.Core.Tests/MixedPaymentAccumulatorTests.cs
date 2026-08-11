using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

/// <summary>
/// Unit tests for <see cref="MixedPaymentAccumulator"/> (Blueprint §3).
///
/// Key scenario tested: customer pays a $2.00 product with $1 USD + 4 100 KHR.
/// </summary>
public sealed class MixedPaymentAccumulatorTests
{
    private readonly DualCurrencyCalculator _calc = new();

    // -----------------------------------------------------------------------
    // AccumulateNote — USD only
    // -----------------------------------------------------------------------

    [Fact]
    public void AccumulateNote_SingleUsdNote_UpdatesAccumulatedAndRemaining()
    {
        var session = new MixedPaymentAccumulator(totalUsd: 2.00m, _calc);

        session.AccumulateNote(Money.Usd(1.00m));

        Assert.Equal(1.00m, session.AccumulatedUsdEquivalent);
        Assert.Equal(1.00m, session.RemainingUsd);
        Assert.False(session.IsComplete);
    }

    [Fact]
    public void AccumulateNote_ExactUsdPayment_IsComplete()
    {
        var session = new MixedPaymentAccumulator(totalUsd: 2.00m, _calc);

        session.AccumulateNote(Money.Usd(2.00m));

        Assert.True(session.IsComplete);
        Assert.Equal(0.00m, session.RemainingUsd);
    }

    // -----------------------------------------------------------------------
    // AccumulateNote — KHR only
    // -----------------------------------------------------------------------

    [Fact]
    public void AccumulateNote_KhrNote_ConvertsToUsdEquivalent()
    {
        var session = new MixedPaymentAccumulator(totalUsd: 1.00m, _calc);

        // 4 100 KHR = 1.00 USD at default rate
        session.AccumulateNote(Money.Khr(4_100m));

        Assert.Equal(1.00m, session.AccumulatedUsdEquivalent);
        Assert.True(session.IsComplete);
    }

    // -----------------------------------------------------------------------
    // AccumulateNote — MIXED payment (the user's exact scenario)
    // -----------------------------------------------------------------------

    [Fact]
    public void AccumulateNote_MixedPayment_UsdThenKhr_CompletesCorrectly()
    {
        // Scenario: product $2.00, customer pays $1 USD then 4 100 KHR
        var session = new MixedPaymentAccumulator(totalUsd: 2.00m, _calc);

        session.AccumulateNote(Money.Usd(1.00m));   // pays first with $1
        Assert.False(session.IsComplete);           // not done yet
        Assert.Equal(1.00m, session.RemainingUsd);

        session.AccumulateNote(Money.Khr(4_100m));  // then pays 4 100 KHR
        Assert.True(session.IsComplete);            // now complete
        Assert.Equal(0.00m, session.RemainingUsd);
    }

    [Fact]
    public void AccumulateNote_MixedPayment_KhrThenUsd_CompletesCorrectly()
    {
        // Scenario: customer pays 4 100 KHR first, then $1 USD
        var session = new MixedPaymentAccumulator(totalUsd: 2.00m, _calc);

        session.AccumulateNote(Money.Khr(4_100m));
        Assert.False(session.IsComplete);

        session.AccumulateNote(Money.Usd(1.00m));
        Assert.True(session.IsComplete);
    }

    // -----------------------------------------------------------------------
    // CalculateChange — after mixed payment
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateChange_ExactMixedPayment_ReturnsNoChange()
    {
        // $1 USD + 4 100 KHR for a $2.00 product → zero change
        var session = new MixedPaymentAccumulator(totalUsd: 2.00m, _calc);
        session.AccumulateNote(Money.Usd(1.00m));
        session.AccumulateNote(Money.Khr(4_100m));

        var change = session.CalculateChange();

        Assert.Empty(change.UsdNotes);
        Assert.Empty(change.KhrNotes);
    }

    [Fact]
    public void CalculateChange_MixedOverpayment_ReturnsCorrectChange()
    {
        // $5 USD + 4 100 KHR for a $2.00 product
        // paid = $5 + $1 = $6 → change = $4.00 in USD
        var session = new MixedPaymentAccumulator(totalUsd: 2.00m, _calc);
        session.AccumulateNote(Money.Usd(5.00m));
        session.AccumulateNote(Money.Khr(4_100m));

        var change = session.CalculateChange();

        var totalUsd = change.UsdNotes.Sum(kv => kv.Key.Amount * kv.Value);
        Assert.Equal(4.00m, totalUsd);
        Assert.Empty(change.KhrNotes);
    }

    // -----------------------------------------------------------------------
    // CalculateChange — guard
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateChange_BeforeIsComplete_Throws()
    {
        var session = new MixedPaymentAccumulator(totalUsd: 2.00m, _calc);
        session.AccumulateNote(Money.Usd(1.00m));   // only partial payment

        Assert.Throws<InvalidOperationException>(() => session.CalculateChange());
    }

    // -----------------------------------------------------------------------
    // RemainingUsd — never goes negative
    // -----------------------------------------------------------------------

    [Fact]
    public void RemainingUsd_NeverGoesNegative_WhenOverpaid()
    {
        var session = new MixedPaymentAccumulator(totalUsd: 1.00m, _calc);
        session.AccumulateNote(Money.Usd(5.00m));

        Assert.Equal(0.00m, session.RemainingUsd);
    }

    // -----------------------------------------------------------------------
    // Reset
    // -----------------------------------------------------------------------

    [Fact]
    public void Reset_ClearsAccumulatedAmount()
    {
        var session = new MixedPaymentAccumulator(totalUsd: 2.00m, _calc);
        session.AccumulateNote(Money.Usd(2.00m));
        Assert.True(session.IsComplete);

        session.Reset(newTotalUsd: 3.00m);

        Assert.Equal(0.00m, session.AccumulatedUsdEquivalent);
        Assert.False(session.IsComplete);
    }

    // -----------------------------------------------------------------------
    // Construction guards
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_ZeroTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MixedPaymentAccumulator(totalUsd: 0m, _calc));
    }

    [Fact]
    public void Constructor_NegativeTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MixedPaymentAccumulator(totalUsd: -1m, _calc));
    }
}
