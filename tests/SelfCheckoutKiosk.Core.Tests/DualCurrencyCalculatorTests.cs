using System;
using System.Collections.Generic;
using System.Linq;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class DualCurrencyCalculatorTests
{
    private readonly DualCurrencyCalculator
        _calculator =
            new();

    [Theory]
    [InlineData(10.00, 10.00)]
    [InlineData(10.00, 9.00)]
    [InlineData(10.00, 0.00)]
    public void CalculateChange_WhenNoOverpayment_ReturnsEmpty(
        decimal totalUsd,
        decimal tenderedUsd)
    {
        var result =
            _calculator.CalculateChange(
                totalUsd,
                tenderedUsd,
                4000m,
                EmptyInventory(),
                EmptyInventory()
            );

        Assert.Empty(
            result.UsdNotes
        );

        Assert.Empty(
            result.KhrNotes
        );
    }

    [Fact]
    public void CalculateChange_SplitsWholeUsdAndFractionalUsd()
    {
        /*
         * Total:     10.25 USD
         * Tendered:  12.00 USD
         * Change:     1.75 USD
         *
         * USD portion:
         * 1.00 USD
         *
         * KHR portion:
         * 0.75 × 4,000 = 3,000 KHR
         */
        var result =
            _calculator.CalculateChange(
                10.25m,
                12.00m,
                4000m,
                Inventory(
                    (
                        Money.Usd(1m),
                        10
                    )
                ),
                Inventory(
                    (
                        Money.Khr(2000m),
                        10
                    ),
                    (
                        Money.Khr(1000m),
                        10
                    )
                )
            );

        Assert.Equal(
            1m,
            CalculateTotal(
                result.UsdNotes
            )
        );

        Assert.Equal(
            3000m,
            CalculateTotal(
                result.KhrNotes
            )
        );

        var usdNote =
            Assert.Single(
                result.UsdNotes
            );

        Assert.Equal(
            Money.Usd(1m),
            usdNote.Key
        );

        Assert.Equal(
            1,
            usdNote.Value
        );

        Assert.Collection(
            result.KhrNotes,
            note =>
            {
                Assert.Equal(
                    Money.Khr(2000m),
                    note.Key
                );

                Assert.Equal(
                    1,
                    note.Value
                );
            },
            note =>
            {
                Assert.Equal(
                    Money.Khr(1000m),
                    note.Key
                );

                Assert.Equal(
                    1,
                    note.Value
                );
            }
        );
    }

    [Fact]
    public void CalculateChange_ConvertsOnlyFractionalUsdToKhr()
    {
        /*
         * Change owed:
         *
         * 3.75 USD
         *
         * Correct:
         * 3 USD + 3,000 KHR
         *
         * Incorrect:
         * Converting the entire 3.75 USD into KHR.
         */
        var result =
            _calculator.CalculateChange(
                10.25m,
                14.00m,
                4000m,
                Inventory(
                    (
                        Money.Usd(1m),
                        10
                    )
                ),
                Inventory(
                    (
                        Money.Khr(1000m),
                        10
                    )
                )
            );

        Assert.Equal(
            3m,
            CalculateTotal(
                result.UsdNotes
            )
        );

        Assert.Equal(
            3000m,
            CalculateTotal(
                result.KhrNotes
            )
        );
    }

    [Fact]
    public void CalculateChange_RoundsKhrDownToDispensableValue()
    {
        /*
         * Fractional change:
         *
         * 0.73 USD × 4,000 = 2,920 KHR
         *
         * Available denominations can dispense:
         *
         * 2,900 KHR
         *
         * The calculator must not round up to 3,000 KHR.
         */
        var result =
            _calculator.CalculateChange(
                10.27m,
                11.00m,
                4000m,
                EmptyInventory(),
                Inventory(
                    (
                        Money.Khr(1000m),
                        10
                    ),
                    (
                        Money.Khr(500m),
                        10
                    ),
                    (
                        Money.Khr(100m),
                        10
                    )
                )
            );

        Assert.Equal(
            2900m,
            CalculateTotal(
                result.KhrNotes
            )
        );

        Assert.True(
            CalculateTotal(
                result.KhrNotes
            ) <= 2920m
        );
    }

    [Fact]
    public void CalculateChange_KhrSelectionDoesNotDependOnGreedyAlgorithm()
    {
        /*
         * Target:
         *
         * 6,000 KHR
         *
         * Greedy selection:
         * 5,000 KHR
         *
         * Better available combination:
         * 2,000 KHR × 3 = 6,000 KHR
         */
        var result =
            _calculator.CalculateChange(
                10.25m,
                11.00m,
                8000m,
                EmptyInventory(),
                Inventory(
                    (
                        Money.Khr(5000m),
                        1
                    ),
                    (
                        Money.Khr(2000m),
                        3
                    )
                )
            );

        Assert.Equal(
            6000m,
            CalculateTotal(
                result.KhrNotes
            )
        );

        var note =
            Assert.Single(
                result.KhrNotes
            );

        Assert.Equal(
            Money.Khr(2000m),
            note.Key
        );

        Assert.Equal(
            3,
            note.Value
        );
    }

    [Fact]
    public void CalculateChange_UsdSelectionDoesNotDependOnGreedyAlgorithm()
    {
        /*
         * Whole-dollar change:
         *
         * 6 USD
         *
         * Greedy selection:
         * 5 USD with no valid completion.
         *
         * Exact available combination:
         * 2 USD × 3 = 6 USD
         */
        var result =
            _calculator.CalculateChange(
                10.00m,
                16.00m,
                4000m,
                Inventory(
                    (
                        Money.Usd(5m),
                        1
                    ),
                    (
                        Money.Usd(2m),
                        3
                    )
                ),
                EmptyInventory()
            );

        Assert.Equal(
            6m,
            CalculateTotal(
                result.UsdNotes
            )
        );

        var note =
            Assert.Single(
                result.UsdNotes
            );

        Assert.Equal(
            Money.Usd(2m),
            note.Key
        );

        Assert.Equal(
            3,
            note.Value
        );
    }

    [Fact]
    public void CalculateChange_WhenExactUsdChangeIsUnavailable_Throws()
    {
        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    _calculator.CalculateChange(
                        10.25m,
                        13.50m,
                        4000m,
                        Inventory(
                            (
                                Money.Usd(5m),
                                1
                            )
                        ),
                        Inventory(
                            (
                                Money.Khr(1000m),
                                10
                            )
                        )
                    )
            );

        Assert.Contains(
            "whole-dollar change exactly",
            exception.Message
        );
    }

    [Fact]
    public void CalculateChange_RespectsAvailableKhrNoteCounts()
    {
        /*
         * The customer is theoretically owed 3,000 KHR.
         *
         * Available:
         * 1,000 KHR × 2
         *   500 KHR × 1
         *
         * Maximum possible dispense:
         * 2,500 KHR
         */
        var result =
            _calculator.CalculateChange(
                10.25m,
                11.00m,
                4000m,
                EmptyInventory(),
                Inventory(
                    (
                        Money.Khr(1000m),
                        2
                    ),
                    (
                        Money.Khr(500m),
                        1
                    )
                )
            );

        Assert.Equal(
            2500m,
            CalculateTotal(
                result.KhrNotes
            )
        );

        Assert.All(
            result.KhrNotes,
            note =>
            {
                if (
                    note.Key ==
                    Money.Khr(1000m)
                )
                {
                    Assert.True(
                        note.Value <= 2
                    );
                }

                if (
                    note.Key ==
                    Money.Khr(500m)
                )
                {
                    Assert.True(
                        note.Value <= 1
                    );
                }
            }
        );
    }

    [Fact]
    public void CalculateChange_WhenNoKhrNotesAreAvailable_ReturnsOnlyUsdChange()
    {
        var result =
            _calculator.CalculateChange(
                10.25m,
                12.00m,
                4000m,
                Inventory(
                    (
                        Money.Usd(1m),
                        10
                    )
                ),
                EmptyInventory()
            );

        Assert.Equal(
            1m,
            CalculateTotal(
                result.UsdNotes
            )
        );

        Assert.Empty(
            result.KhrNotes
        );
    }

    [Fact]
    public void CalculateChange_NeverReturnsMoreValueThanCustomerIsOwed()
    {
        const decimal totalUsd =
            10.27m;

        const decimal tenderedUsd =
            14.00m;

        const decimal rate =
            4100m;

        var result =
            _calculator.CalculateChange(
                totalUsd,
                tenderedUsd,
                rate,
                Inventory(
                    (
                        Money.Usd(2m),
                        1
                    ),
                    (
                        Money.Usd(1m),
                        10
                    )
                ),
                Inventory(
                    (
                        Money.Khr(2000m),
                        10
                    ),
                    (
                        Money.Khr(1000m),
                        10
                    ),
                    (
                        Money.Khr(500m),
                        10
                    ),
                    (
                        Money.Khr(100m),
                        10
                    )
                )
            );

        var usdDispensed =
            CalculateTotal(
                result.UsdNotes
            );

        var khrDispensed =
            CalculateTotal(
                result.KhrNotes
            );

        var dispensedUsdEquivalent =
            usdDispensed +
            (
                khrDispensed /
                rate
            );

        var changeOwedUsd =
            tenderedUsd -
            totalUsd;

        Assert.True(
            dispensedUsdEquivalent <=
            changeOwedUsd,
            $"Dispensed equivalent {dispensedUsdEquivalent} USD " +
            $"exceeded owed change {changeOwedUsd} USD."
        );
    }

    [Fact]
    public void CalculateChange_IsDeterministicRegardlessOfInventoryOrder()
    {
        var firstResult =
            _calculator.CalculateChange(
                10.25m,
                14.00m,
                4000m,
                Inventory(
                    (
                        Money.Usd(1m),
                        10
                    ),
                    (
                        Money.Usd(2m),
                        10
                    )
                ),
                Inventory(
                    (
                        Money.Khr(500m),
                        10
                    ),
                    (
                        Money.Khr(2000m),
                        10
                    ),
                    (
                        Money.Khr(1000m),
                        10
                    )
                )
            );

        var secondResult =
            _calculator.CalculateChange(
                10.25m,
                14.00m,
                4000m,
                Inventory(
                    (
                        Money.Usd(2m),
                        10
                    ),
                    (
                        Money.Usd(1m),
                        10
                    )
                ),
                Inventory(
                    (
                        Money.Khr(1000m),
                        10
                    ),
                    (
                        Money.Khr(500m),
                        10
                    ),
                    (
                        Money.Khr(2000m),
                        10
                    )
                )
            );

        Assert.Equal(
            firstResult.UsdNotes.ToArray(),
            secondResult.UsdNotes.ToArray()
        );

        Assert.Equal(
            firstResult.KhrNotes.ToArray(),
            secondResult.KhrNotes.ToArray()
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CalculateChange_WhenExchangeRateIsNotPositive_Throws(
        decimal exchangeRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                _calculator.CalculateChange(
                    10m,
                    10m,
                    exchangeRate,
                    EmptyInventory(),
                    EmptyInventory()
                )
        );
    }

    [Theory]
    [InlineData(-1.00, 10.00)]
    [InlineData(10.00, -1.00)]
    public void CalculateChange_WhenUsdAmountIsNegative_Throws(
        decimal totalUsd,
        decimal tenderedUsd)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                _calculator.CalculateChange(
                    totalUsd,
                    tenderedUsd,
                    4000m,
                    EmptyInventory(),
                    EmptyInventory()
                )
        );
    }

    [Theory]
    [InlineData(10.001, 11.00)]
    [InlineData(10.00, 11.001)]
    public void CalculateChange_WhenUsdPrecisionIsUnsupported_Throws(
        decimal totalUsd,
        decimal tenderedUsd)
    {
        Assert.Throws<ArgumentException>(
            () =>
                _calculator.CalculateChange(
                    totalUsd,
                    tenderedUsd,
                    4000m,
                    EmptyInventory(),
                    EmptyInventory()
                )
        );
    }

    [Fact]
    public void CalculateChange_WhenUsdInventoryContainsKhr_Throws()
    {
        Assert.Throws<ArgumentException>(
            () =>
                _calculator.CalculateChange(
                    10m,
                    11m,
                    4000m,
                    Inventory(
                        (
                            Money.Khr(1000m),
                            1
                        )
                    ),
                    EmptyInventory()
                )
        );
    }

    [Fact]
    public void CalculateChange_WhenKhrInventoryContainsUsd_Throws()
    {
        Assert.Throws<ArgumentException>(
            () =>
                _calculator.CalculateChange(
                    10m,
                    10.50m,
                    4000m,
                    EmptyInventory(),
                    Inventory(
                        (
                            Money.Usd(1m),
                            1
                        )
                    )
                )
        );
    }

    [Fact]
    public void CalculateChange_WhenUsdDenominationIsFractional_Throws()
    {
        Assert.Throws<ArgumentException>(
            () =>
                _calculator.CalculateChange(
                    10m,
                    11m,
                    4000m,
                    Inventory(
                        (
                            Money.Usd(0.50m),
                            10
                        )
                    ),
                    EmptyInventory()
                )
        );
    }

    [Fact]
    public void CalculateChange_WhenInventoryContainsDuplicateDenomination_Throws()
    {
        Assert.Throws<ArgumentException>(
            () =>
                _calculator.CalculateChange(
                    10m,
                    11m,
                    4000m,
                    Inventory(
                        (
                            Money.Usd(1m),
                            5
                        ),
                        (
                            Money.Usd(1m),
                            10
                        )
                    ),
                    EmptyInventory()
                )
        );
    }

    [Fact]
    public void CalculateChange_WhenAvailableCountIsNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                _calculator.CalculateChange(
                    10m,
                    11m,
                    4000m,
                    Inventory(
                        (
                            Money.Usd(1m),
                            -1
                        )
                    ),
                    EmptyInventory()
                )
        );
    }

    [Fact]
    public void CalculateChange_WhenUsdInventoryIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                _calculator.CalculateChange(
                    10m,
                    11m,
                    4000m,
                    null!,
                    EmptyInventory()
                )
        );
    }

    [Fact]
    public void CalculateChange_WhenKhrInventoryIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                _calculator.CalculateChange(
                    10m,
                    11m,
                    4000m,
                    EmptyInventory(),
                    null!
                )
        );
    }

    private static IReadOnlyList<
        KeyValuePair<Money, int>>
        Inventory(
            params (
                Money Denomination,
                int Count
            )[] notes)
    {
        return notes
            .Select(
                note =>
                    new KeyValuePair<
                        Money,
                        int
                    >(
                        note.Denomination,
                        note.Count
                    )
            )
            .ToArray();
    }

    private static IReadOnlyList<
        KeyValuePair<Money, int>>
        EmptyInventory()
    {
        return Array.Empty<
            KeyValuePair<Money, int>
        >();
    }

    private static decimal CalculateTotal(
        IReadOnlyList<
            KeyValuePair<Money, int>>
                notes)
    {
        return notes.Sum(
            note =>
                note.Key.Amount *
                note.Value
        );
    }
}