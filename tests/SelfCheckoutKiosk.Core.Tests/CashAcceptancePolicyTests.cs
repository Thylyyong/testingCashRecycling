using System;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class CashAcceptancePolicyTests
{
    private const decimal UsdToKhrRate =
        4100m;

    private readonly CashAcceptancePolicy
        _policy =
            new();

    [Fact]
    public void Evaluate_KhrNoteBelowTarget_AcceptsAndContinues()
    {
        var result =
            _policy.Evaluate(
                targetKhr:
                    Money.Khr(
                        5300m
                    ),
                currentlyPaidKhr:
                    Money.Khr(
                        0m
                    ),
                escrowedNote:
                    Money.Khr(
                        5000m
                    ),
                usdToKhrRate:
                    UsdToKhrRate
            );

        Assert.Equal(
            CashAcceptanceDecision
                .AcceptAndContinue,
            result.Decision
        );

        Assert.True(
            result.ShouldAcceptNote
        );

        Assert.False(
            result.IsPaymentComplete
        );

        Assert.Equal(
            Money.Khr(
                5000m
            ),
            result.NoteValueKhr
        );

        Assert.Equal(
            Money.Khr(
                5000m
            ),
            result.ProposedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                5000m
            ),
            result.AcceptedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                300m
            ),
            result.RemainingKhr
        );
    }

    [Fact]
    public void Evaluate_KhrNoteExactlyReachesTarget_AcceptsAndCompletes()
    {
        var result =
            _policy.Evaluate(
                targetKhr:
                    Money.Khr(
                        5300m
                    ),
                currentlyPaidKhr:
                    Money.Khr(
                        5000m
                    ),
                escrowedNote:
                    Money.Khr(
                        300m
                    ),
                usdToKhrRate:
                    UsdToKhrRate
            );

        Assert.Equal(
            CashAcceptanceDecision
                .AcceptAndComplete,
            result.Decision
        );

        Assert.True(
            result.ShouldAcceptNote
        );

        Assert.True(
            result.IsPaymentComplete
        );

        Assert.Equal(
            Money.Khr(
                5300m
            ),
            result.ProposedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                5300m
            ),
            result.AcceptedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                0m
            ),
            result.RemainingKhr
        );
    }

    [Fact]
    public void Evaluate_KhrNoteCausesOverpayment_RejectsNote()
    {
        var result =
            _policy.Evaluate(
                targetKhr:
                    Money.Khr(
                        5300m
                    ),
                currentlyPaidKhr:
                    Money.Khr(
                        5000m
                    ),
                escrowedNote:
                    Money.Khr(
                        1000m
                    ),
                usdToKhrRate:
                    UsdToKhrRate
            );

        Assert.Equal(
            CashAcceptanceDecision
                .RejectOverpayment,
            result.Decision
        );

        Assert.False(
            result.ShouldAcceptNote
        );

        Assert.False(
            result.IsPaymentComplete
        );

        /*
         * The proposed value includes the rejected note so the
         * decision remains auditable.
         */
        Assert.Equal(
            Money.Khr(
                6000m
            ),
            result.ProposedPaidKhr
        );

        /*
         * The accepted total must remain unchanged because the
         * escrowed note was rejected.
         */
        Assert.Equal(
            Money.Khr(
                5000m
            ),
            result.AcceptedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                300m
            ),
            result.RemainingKhr
        );
    }

    [Fact]
    public void Evaluate_UsdNoteBelowTarget_ConvertsAndContinues()
    {
        var result =
            _policy.Evaluate(
                targetKhr:
                    Money.Khr(
                        10000m
                    ),
                currentlyPaidKhr:
                    Money.Khr(
                        0m
                    ),
                escrowedNote:
                    Money.Usd(
                        1m
                    ),
                usdToKhrRate:
                    UsdToKhrRate
            );

        Assert.Equal(
            CashAcceptanceDecision
                .AcceptAndContinue,
            result.Decision
        );

        Assert.Equal(
            Money.Khr(
                4100m
            ),
            result.NoteValueKhr
        );

        Assert.Equal(
            Money.Khr(
                4100m
            ),
            result.AcceptedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                5900m
            ),
            result.RemainingKhr
        );
    }

    [Fact]
    public void Evaluate_MixedCurrencyExactlyReachesTarget_CompletesPayment()
    {
        /*
         * Previously accepted:
         * 5,000 KHR
         *
         * New note:
         * 1 USD = 4,100 KHR
         *
         * Final:
         * 9,100 KHR
         */
        var result =
            _policy.Evaluate(
                targetKhr:
                    Money.Khr(
                        9100m
                    ),
                currentlyPaidKhr:
                    Money.Khr(
                        5000m
                    ),
                escrowedNote:
                    Money.Usd(
                        1m
                    ),
                usdToKhrRate:
                    UsdToKhrRate
            );

        Assert.Equal(
            CashAcceptanceDecision
                .AcceptAndComplete,
            result.Decision
        );

        Assert.Equal(
            Money.Khr(
                4100m
            ),
            result.NoteValueKhr
        );

        Assert.Equal(
            Money.Khr(
                9100m
            ),
            result.AcceptedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                0m
            ),
            result.RemainingKhr
        );
    }

    [Fact]
    public void Evaluate_UsdNoteCausesOverpayment_RejectsNote()
    {
        /*
         * Target:
         * 9,000 KHR
         *
         * Accepted:
         * 5,000 KHR
         *
         * New USD note:
         * 8,200 KHR
         *
         * Proposed:
         * 13,200 KHR
         */
        var result =
            _policy.Evaluate(
                targetKhr:
                    Money.Khr(
                        9000m
                    ),
                currentlyPaidKhr:
                    Money.Khr(
                        5000m
                    ),
                escrowedNote:
                    Money.Usd(
                        2m
                    ),
                usdToKhrRate:
                    UsdToKhrRate
            );

        Assert.Equal(
            CashAcceptanceDecision
                .RejectOverpayment,
            result.Decision
        );

        Assert.Equal(
            Money.Khr(
                13200m
            ),
            result.ProposedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                5000m
            ),
            result.AcceptedPaidKhr
        );

        Assert.Equal(
            Money.Khr(
                4000m
            ),
            result.RemainingKhr
        );
    }

    [Fact]
    public void Evaluate_TargetIsNotKhr_ThrowsArgumentException()
    {
        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    _policy.Evaluate(
                        targetKhr:
                            Money.Usd(
                                5m
                            ),
                        currentlyPaidKhr:
                            Money.Khr(
                                0m
                            ),
                        escrowedNote:
                            Money.Khr(
                                1000m
                            ),
                        usdToKhrRate:
                            UsdToKhrRate
                    )
            );

        Assert.Equal(
            "targetKhr",
            exception.ParamName
        );
    }

    [Fact]
    public void Evaluate_CurrentlyPaidIsNotKhr_ThrowsArgumentException()
    {
        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    _policy.Evaluate(
                        targetKhr:
                            Money.Khr(
                                10000m
                            ),
                        currentlyPaidKhr:
                            Money.Usd(
                                1m
                            ),
                        escrowedNote:
                            Money.Khr(
                                1000m
                            ),
                        usdToKhrRate:
                            UsdToKhrRate
                    )
            );

        Assert.Equal(
            "currentlyPaidKhr",
            exception.ParamName
        );
    }

    [Fact]
    public void Evaluate_ZeroTarget_ThrowsArgumentOutOfRangeException()
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    _policy.Evaluate(
                        targetKhr:
                            Money.Khr(
                                0m
                            ),
                        currentlyPaidKhr:
                            Money.Khr(
                                0m
                            ),
                        escrowedNote:
                            Money.Khr(
                                1000m
                            ),
                        usdToKhrRate:
                            UsdToKhrRate
                    )
            );

        Assert.Equal(
            "targetKhr",
            exception.ParamName
        );
    }

    [Fact]
    public void Evaluate_PreviouslyPaidExceedsTarget_ThrowsArgumentOutOfRangeException()
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    _policy.Evaluate(
                        targetKhr:
                            Money.Khr(
                                5000m
                            ),
                        currentlyPaidKhr:
                            Money.Khr(
                                5100m
                            ),
                        escrowedNote:
                            Money.Khr(
                                100m
                            ),
                        usdToKhrRate:
                            UsdToKhrRate
                    )
            );

        Assert.Equal(
            "currentlyPaidKhr",
            exception.ParamName
        );
    }

    [Fact]
    public void Evaluate_ZeroValueNote_ThrowsArgumentException()
    {
        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    _policy.Evaluate(
                        targetKhr:
                            Money.Khr(
                                5000m
                            ),
                        currentlyPaidKhr:
                            Money.Khr(
                                0m
                            ),
                        escrowedNote:
                            Money.Khr(
                                0m
                            ),
                        usdToKhrRate:
                            UsdToKhrRate
                    )
            );

        Assert.Equal(
            "escrowedNote",
            exception.ParamName
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Evaluate_InvalidExchangeRate_ThrowsArgumentOutOfRangeException(
        int invalidRate)
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    _policy.Evaluate(
                        targetKhr:
                            Money.Khr(
                                5000m
                            ),
                        currentlyPaidKhr:
                            Money.Khr(
                                0m
                            ),
                        escrowedNote:
                            Money.Usd(
                                1m
                            ),
                        usdToKhrRate:
                            invalidRate
                    )
            );

        Assert.Equal(
            "usdToKhrRate",
            exception.ParamName
        );
    }

    [Fact]
    public void Evaluate_UsdConversionProducesFractionalKhr_ThrowsArgumentException()
    {
        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    _policy.Evaluate(
                        targetKhr:
                            Money.Khr(
                                10000m
                            ),
                        currentlyPaidKhr:
                            Money.Khr(
                                0m
                            ),
                        escrowedNote:
                            Money.Usd(
                                1m
                            ),
                        usdToKhrRate:
                            4100.50m
                    )
            );

        Assert.Equal(
            "usdToKhrRate",
            exception.ParamName
        );
    }
}