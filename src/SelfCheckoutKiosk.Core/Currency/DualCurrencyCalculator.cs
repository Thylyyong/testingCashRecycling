using System;
using System.Collections.Generic;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// Calculates deterministic dual-currency cash change.
///
/// Policy:
/// - USD is the transaction ledger currency.
/// - Whole-dollar overpayment is returned using USD notes.
/// - Only the fractional USD overpayment is converted to KHR.
/// - KHR change is always rounded down.
/// - Only available denominations may be selected.
/// - The calculated value never exceeds the value owed.
/// </summary>
public sealed class DualCurrencyCalculator
{
    /// <summary>
    /// Calculates the physical notes that should be dispensed as change.
    /// </summary>
    /// <param name="totalUsd">
    /// Transaction total in USD.
    /// </param>
    /// <param name="tenderedUsd">
    /// Total amount tendered by the customer in USD.
    /// </param>
    /// <param name="usdToKhrRate">
    /// Number of KHR represented by one USD.
    /// </param>
    /// <param name="availableUsdNotes">
    /// Available USD denomination and note-count pairs.
    /// </param>
    /// <param name="availableKhrNotes">
    /// Available KHR denomination and note-count pairs.
    /// </param>
    /// <returns>
    /// The USD and KHR notes that should be dispensed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when an inventory collection is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an amount is negative, the exchange rate is not
    /// positive, or an available count is negative.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when money precision, currency, or denomination data is
    /// invalid.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the required whole-dollar USD change cannot be
    /// produced exactly from the available USD notes.
    /// </exception>
    public ChangeBreakdown CalculateChange(
        decimal totalUsd,
        decimal tenderedUsd,
        decimal usdToKhrRate,
        IReadOnlyList<KeyValuePair<Money, int>>
            availableUsdNotes,
        IReadOnlyList<KeyValuePair<Money, int>>
            availableKhrNotes)
    {
        ArgumentNullException.ThrowIfNull(
            availableUsdNotes
        );

        ArgumentNullException.ThrowIfNull(
            availableKhrNotes
        );

        /*
         * Validate USD precision and reject negative values by using the
         * existing Domain value object.
         */
        _ = Money.Usd(
            totalUsd
        );

        _ = Money.Usd(
            tenderedUsd
        );

        if (usdToKhrRate <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usdToKhrRate),
                usdToKhrRate,
                "The USD-to-KHR exchange rate must be greater than zero."
            );
        }

        var usdInventory =
            ValidateAndNormalizeInventory(
                availableUsdNotes,
                CurrencyCode.Usd,
                requireWholeDenomination: true,
                nameof(availableUsdNotes)
            );

        var khrInventory =
            ValidateAndNormalizeInventory(
                availableKhrNotes,
                CurrencyCode.Khr,
                requireWholeDenomination: true,
                nameof(availableKhrNotes)
            );

        /*
         * Required balance formula:
         *
         * remaining = max(0, total - tendered)
         */
        var remainingUsd =
            Math.Max(
                0m,
                totalUsd - tenderedUsd
            );

        /*
         * The customer still owes money, or paid the exact amount.
         * No change is required.
         */
        if (
            remainingUsd > 0m ||
            tenderedUsd == totalUsd
        )
        {
            return ChangeBreakdown.Empty;
        }

        var overpaymentUsd =
            tenderedUsd - totalUsd;

        /*
         * Whole USD is returned as USD notes.
         *
         * Example:
         * $3.75 overpayment
         * -> $3.00 USD-note portion
         * -> $0.75 KHR-conversion portion
         */
        var wholeDollarUsd =
            decimal.Floor(
                overpaymentUsd
            );

        var fractionalUsd =
            overpaymentUsd -
            wholeDollarUsd;

        var usdSelection =
            FindBestCombinationAtOrBelow(
                wholeDollarUsd,
                usdInventory
            );

        /*
         * Whole-dollar USD change must be exact.
         *
         * We must not:
         * - silently underpay the USD portion;
         * - convert the whole-dollar portion into KHR;
         * - dispense more USD than the customer is owed.
         */
        if (
            usdSelection.TotalValue !=
            wholeDollarUsd
        )
        {
            throw new InvalidOperationException(
                "Available USD denominations cannot produce the " +
                "required whole-dollar change exactly."
            );
        }

        /*
         * Only the fractional USD portion is converted to KHR.
         *
         * Floor removes any sub-riel fraction before physical
         * denominations are selected.
         */
        var maximumKhrOwed =
            decimal.Floor(
                fractionalUsd *
                usdToKhrRate
            );

        /*
         * Find the greatest dispensable KHR value that is:
         *
         * - no greater than maximumKhrOwed;
         * - possible with the available note counts.
         *
         * This can be lower than maximumKhrOwed because KHR change is
         * deliberately rounded down.
         */
        var khrSelection =
            FindBestCombinationAtOrBelow(
                maximumKhrOwed,
                khrInventory
            );

        var usdNotes =
            CreateNoteInstructions(
                usdInventory,
                usdSelection.Counts,
                CurrencyCode.Usd
            );

        var khrNotes =
            CreateNoteInstructions(
                khrInventory,
                khrSelection.Counts,
                CurrencyCode.Khr
            );

        if (
            usdNotes.Count == 0 &&
            khrNotes.Count == 0
        )
        {
            return ChangeBreakdown.Empty;
        }

        return new ChangeBreakdown(
            usdNotes,
            khrNotes
        );
    }

    /// <summary>
    /// Validates cassette inventory and sorts denominations from highest
    /// to lowest so output is deterministic regardless of input order.
    /// </summary>
    private static IReadOnlyList<AvailableDenomination>
        ValidateAndNormalizeInventory(
            IReadOnlyList<KeyValuePair<Money, int>>
                availableNotes,
            CurrencyCode expectedCurrency,
            bool requireWholeDenomination,
            string parameterName)
    {
        var normalized =
            new List<AvailableDenomination>(
                availableNotes.Count
            );

        var usedDenominations =
            new HashSet<decimal>();

        foreach (var availableNote in availableNotes)
        {
            var denomination =
                availableNote.Key;

            var availableCount =
                availableNote.Value;

            if (
                denomination.Currency !=
                expectedCurrency
            )
            {
                throw new ArgumentException(
                    $"The {parameterName} collection must contain " +
                    $"only {expectedCurrency} denominations.",
                    parameterName
                );
            }

            if (denomination.Amount <= 0m)
            {
                throw new ArgumentException(
                    "A denomination must be greater than zero.",
                    parameterName
                );
            }

            if (
                requireWholeDenomination &&
                decimal.Truncate(
                    denomination.Amount
                ) != denomination.Amount
            )
            {
                throw new ArgumentException(
                    $"The {expectedCurrency} cash denomination " +
                    "must be a whole value.",
                    parameterName
                );
            }

            if (availableCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    availableCount,
                    "An available note count cannot be negative."
                );
            }

            if (
                !usedDenominations.Add(
                    denomination.Amount
                )
            )
            {
                throw new ArgumentException(
                    $"Duplicate denomination detected: " +
                    $"{denomination.Amount} {expectedCurrency}.",
                    parameterName
                );
            }

            /*
             * Zero-count entries are valid inventory observations, but
             * they cannot contribute to a dispense result.
             */
            if (availableCount == 0)
            {
                continue;
            }

            normalized.Add(
                new AvailableDenomination(
                    denomination.Amount,
                    availableCount
                )
            );
        }

        normalized.Sort(
            static (
                left,
                right) =>
                right.Amount.CompareTo(
                    left.Amount
                )
        );

        return normalized;
    }

    /// <summary>
    /// Finds the greatest value at or below the target using bounded
    /// denomination counts.
    ///
    /// This intentionally does not use a simple greedy algorithm because
    /// greedy selection can miss a better valid combination when cassette
    /// counts are limited.
    /// </summary>
    private static CombinationSelection
        FindBestCombinationAtOrBelow(
            decimal targetValue,
            IReadOnlyList<AvailableDenomination>
                denominations)
    {
        if (
            targetValue <= 0m ||
            denominations.Count == 0
        )
        {
            return new CombinationSelection(
                0m,
                new int[
                    denominations.Count
                ]
            );
        }

        var memoizedResults =
            new Dictionary<
                (int Index, decimal Remaining),
                CombinationSelection
            >();

        return FindBest(
            0,
            targetValue
        );

        CombinationSelection FindBest(
            int index,
            decimal remaining)
        {
            if (
                index >= denominations.Count ||
                remaining <= 0m
            )
            {
                return new CombinationSelection(
                    0m,
                    new int[
                        denominations.Count
                    ]
                );
            }

            var memoizationKey =
                (
                    Index: index,
                    Remaining: remaining
                );

            if (
                memoizedResults.TryGetValue(
                    memoizationKey,
                    out var memoizedResult
                )
            )
            {
                return memoizedResult;
            }

            var denomination =
                denominations[index];

            var maximumUsableCount =
                GetMaximumUsableCount(
                    denomination,
                    remaining
                );

            CombinationSelection?
                bestSelection = null;

            /*
             * Start with the largest possible count.
             *
             * When two combinations have the same value, this preserves
             * the deterministic preference for higher denominations.
             */
            for (
                var usedCount =
                    maximumUsableCount;
                usedCount >= 0;
                usedCount--)
            {
                var usedValue =
                    denomination.Amount *
                    usedCount;

                var remainingAfterUse =
                    remaining -
                    usedValue;

                var childSelection =
                    FindBest(
                        index + 1,
                        remainingAfterUse
                    );

                var combinedValue =
                    usedValue +
                    childSelection.TotalValue;

                if (
                    bestSelection is null ||
                    combinedValue >
                    bestSelection.TotalValue
                )
                {
                    var combinedCounts =
                        (int[])
                        childSelection.Counts.Clone();

                    combinedCounts[index] =
                        usedCount;

                    bestSelection =
                        new CombinationSelection(
                            combinedValue,
                            combinedCounts
                        );

                    /*
                     * No combination can be better than using the full
                     * remaining target value.
                     */
                    if (
                        combinedValue ==
                        remaining
                    )
                    {
                        break;
                    }
                }
            }

            var finalSelection =
                bestSelection ??
                new CombinationSelection(
                    0m,
                    new int[
                        denominations.Count
                    ]
                );

            memoizedResults[
                memoizationKey
            ] = finalSelection;

            return finalSelection;
        }
    }

    private static int GetMaximumUsableCount(
        AvailableDenomination denomination,
        decimal remaining)
    {
        var maximumByRemaining =
            decimal.Floor(
                remaining /
                denomination.Amount
            );

        if (
            maximumByRemaining >=
            denomination.AvailableCount
        )
        {
            return denomination.AvailableCount;
        }

        return decimal.ToInt32(
            maximumByRemaining
        );
    }

    private static IReadOnlyList<
        KeyValuePair<Money, int>>
        CreateNoteInstructions(
            IReadOnlyList<AvailableDenomination>
                denominations,
            IReadOnlyList<int> selectedCounts,
            CurrencyCode currency)
    {
        var instructions =
            new List<
                KeyValuePair<Money, int>
            >();

        for (
            var index = 0;
            index < denominations.Count;
            index++)
        {
            var selectedCount =
                selectedCounts[index];

            if (selectedCount <= 0)
            {
                continue;
            }

            var denomination =
                new Money(
                    denominations[index].Amount,
                    currency
                );

            instructions.Add(
                new KeyValuePair<Money, int>(
                    denomination,
                    selectedCount
                )
            );
        }

        return instructions;
    }

    private readonly record struct
        AvailableDenomination(
            decimal Amount,
            int AvailableCount
        );

    private sealed record
        CombinationSelection(
            decimal TotalValue,
            int[] Counts
        );
}