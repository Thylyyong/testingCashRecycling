using System;
using System.Threading;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Optional cash-device capability for software-controlled and
/// physically confirmed note escrow.
///
/// A concrete cash adapter implements this interface only when the
/// hardware can:
///
/// - hold a note in escrow;
/// - receive a command to move the note into accepted storage;
/// - confirm whether the note was committed or rejected.
///
/// LLCoreLogicEngine remains the only subscriber to this event.
/// </summary>
public interface ICashEscrowController
{
    /// <summary>
    /// Raised after the device confirms that the currently escrowed
    /// note was physically committed to the vault or rejected back
    /// to the customer.
    /// </summary>
    event EventHandler<CashEscrowResolvedEventArgs>?
        OnEscrowResolved;

    /// <summary>
    /// Commands the device to move the currently escrowed note into
    /// accepted cash storage.
    ///
    /// Successful task completion means the command was delivered.
    /// Core must wait for OnEscrowResolved before treating the note
    /// as physically committed.
    /// </summary>
    Task CommitEscrowedNoteAsync(
        CancellationToken cancellationToken = default
    );
}