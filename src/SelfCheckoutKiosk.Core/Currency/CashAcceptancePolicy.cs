using System;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// Determines whether one escrowed USD or KHR note may be accepted
/// by a cash-acceptor-only kiosk.
///
/// Policy:
///
/// - The kiosk accepts USD, KHR, or a combination of both.
/// - KHR is used as the internal cash-comparison currency.
/// - USD notes are converted using the transaction exchange rate.
/// - A note is accepted when it keeps the paid amount below the
///   required amount.
/// - A note completes payment when it makes the paid amount exactly
///   equal to the required amount.
/// - A note is rejected when it would cause overpayment.
/// - No change is calculated or dispensed.
/// </summary>
public sealed class CashAcceptancePolicy
{
    /// <summary>
    /// Evaluates one note currently held in hardware escrow.
    /// </summary>
    /// <param name="targetKhr">
    /// Exact cash amount required for the transaction, expressed in
    /// KHR.
    /// </param>
    /// <param name="currentlyPaidKhr">
    /// Cash that has already been physically accepted, expressed in
    /// KHR.
    /// </param>
    /// <param name="escrowedNote">
    /// USD or KHR note currently held in hardware escrow.
    /// </param>
    /// <param name="usdToKhrRate">
    /// Number of KHR represented by one USD for this transaction.
    ///
    /// Example: 4100 means 1 USD = 4,100 KHR.
    /// </param>
    /// <returns>
    /// A deterministic acceptance decision and the resulting KHR
    /// amounts.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the target or paid amount is not KHR, the
    /// escrowed note has zero value, or USD conversion would produce
    /// a fractional KHR amount.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the target is zero, the exchange rate is not
    /// positive, or the previously paid amount is greater than the
    /// required amount.
    /// </exception>
    public CashAcceptanceResult Evaluate(
        Money targetKhr,
        Money currentlyPaidKhr,
        Money escrowedNote,
        decimal usdToKhrRate)
    {
        ValidateKhrAmount(
            targetKhr,
            nameof(targetKhr)
        );

        ValidateKhrAmount(
            currentlyPaidKhr,
            nameof(currentlyPaidKhr)
        );

        if (targetKhr.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetKhr),
                targetKhr.Amount,
                "The required cash amount must be greater than zero."
            );
        }

        if (
            currentlyPaidKhr.Amount >
            targetKhr.Amount
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentlyPaidKhr),
                currentlyPaidKhr.Amount,
                "The previously accepted cash cannot exceed the " +
                "required cash amount."
            );
        }

        if (escrowedNote.Amount <= 0m)
        {
            throw new ArgumentException(
                "The escrowed note amount must be greater than zero.",
                nameof(escrowedNote)
            );
        }

        if (usdToKhrRate <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usdToKhrRate),
                usdToKhrRate,
                "The USD-to-KHR exchange rate must be greater than zero."
            );
        }

        var noteValueKhr =
            NormalizeNoteToKhr(
                escrowedNote,
                usdToKhrRate
            );

        var proposedPaidKhr =
            currentlyPaidKhr.Add(
                noteValueKhr
            );

        var overpaymentKhr = proposedPaidKhr.Amount - targetKhr.Amount;

        if (overpaymentKhr > 500m)
        {
            /*
             * The note must be rejected.
             *
             * Because it is not accepted, the actual running total
             * remains unchanged.
             */
            var remainingAfterRejection =
                targetKhr.Subtract(
                    currentlyPaidKhr
                );

            return new CashAcceptanceResult(
                CashAcceptanceDecision
                    .RejectOverpayment,
                noteValueKhr,
                proposedPaidKhr,
                currentlyPaidKhr,
                remainingAfterRejection
            );
        }

        if (proposedPaidKhr.Amount >= targetKhr.Amount)
        {
            return new CashAcceptanceResult(
                CashAcceptanceDecision
                    .AcceptAndComplete,
                noteValueKhr,
                proposedPaidKhr,
                proposedPaidKhr,
                Money.Khr(
                    0m
                )
            );
        }

        var remainingAfterAcceptance =
            targetKhr.Subtract(
                proposedPaidKhr
            );

        return new CashAcceptanceResult(
            CashAcceptanceDecision
                .AcceptAndContinue,
            noteValueKhr,
            proposedPaidKhr,
            proposedPaidKhr,
            remainingAfterAcceptance
        );
    }

    /// <summary>
    /// Converts one supported note into its KHR comparison value.
    /// </summary>
    private static Money NormalizeNoteToKhr(
        Money note,
        decimal usdToKhrRate)
    {
        switch (note.Currency)
        {
            case CurrencyCode.Khr:
                {
                    return note;
                }

            case CurrencyCode.Usd:
                {
                    var convertedAmountKhr =
                        note.Amount *
                        usdToKhrRate;

                    /*
                     * Money.Khr requires whole riel values.
                     *
                     * Do not silently round an accepted note's value
                     * because that would alter the configured exchange
                     * rate.
                     */
                    if (
                        decimal.Truncate(
                            convertedAmountKhr
                        ) !=
                        convertedAmountKhr
                    )
                    {
                        throw new ArgumentException(
                            "The USD note and exchange rate produce " +
                            "a fractional KHR value. Cash acceptance " +
                            "requires a whole-KHR conversion.",
                            nameof(usdToKhrRate)
                        );
                    }

                    return Money.Khr(
                        convertedAmountKhr
                    );
                }

            default:
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(note),
                        note.Currency,
                        "The escrowed note currency is not supported."
                    );
                }
        }
    }

    private static void ValidateKhrAmount(
        Money money,
        string parameterName)
    {
        if (
            money.Currency !=
            CurrencyCode.Khr
        )
        {
            throw new ArgumentException(
                "The amount must use KHR as its currency.",
                parameterName
            );
        }
    }
}