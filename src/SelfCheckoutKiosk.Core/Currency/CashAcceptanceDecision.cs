using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// Decision produced after evaluating one note currently held
/// in the cash acceptor's escrow position.
/// </summary>
public enum CashAcceptanceDecision
{
    /// <summary>
    /// The note does not exceed the required amount.
    ///
    /// Accept the note, update the running paid amount, and continue
    /// waiting for additional cash.
    /// </summary>
    AcceptAndContinue = 0,

    /// <summary>
    /// The note makes the running paid amount exactly equal to the
    /// required cash amount.
    ///
    /// Accept the note and complete cash payment.
    /// </summary>
    AcceptAndComplete = 1,

    /// <summary>
    /// The note would make the customer pay more than the required
    /// amount.
    ///
    /// Reject the note and do not update the running paid amount.
    /// </summary>
    RejectOverpayment = 2
}

/// <summary>
/// Immutable result of evaluating one escrowed cash note.
///
/// All calculated amounts use KHR as the internal comparison
/// currency.
/// </summary>
/// <param name="Decision">
/// Whether the note should be accepted, accepted as the final note,
/// or rejected because it would cause overpayment.
/// </param>
/// <param name="NoteValueKhr">
/// Value of the escrowed USD or KHR note after normalization to KHR.
/// </param>
/// <param name="ProposedPaidKhr">
/// Running paid amount that would result if the note were accepted.
/// </param>
/// <param name="AcceptedPaidKhr">
/// Actual running paid amount after applying the decision.
///
/// For a rejected note, this remains equal to the amount paid before
/// the note entered escrow.
/// </param>
/// <param name="RemainingKhr">
/// Amount still required after applying the decision.
///
/// For a rejected note, this is calculated from the previously
/// accepted amount because the rejected note is not included.
/// </param>
public sealed record CashAcceptanceResult(
    CashAcceptanceDecision Decision,
    Money NoteValueKhr,
    Money ProposedPaidKhr,
    Money AcceptedPaidKhr,
    Money RemainingKhr)
{
    /// <summary>
    /// Indicates whether the escrowed note should be moved into the
    /// accepted cash storage.
    /// </summary>
    public bool ShouldAcceptNote =>
        Decision !=
        CashAcceptanceDecision.RejectOverpayment;

    /// <summary>
    /// Indicates whether the accepted cash now exactly satisfies the
    /// required payment amount.
    /// </summary>
    public bool IsPaymentComplete =>
        Decision ==
        CashAcceptanceDecision.AcceptAndComplete;
}