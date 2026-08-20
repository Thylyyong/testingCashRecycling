using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.App.Services;

public interface IPaymentService : INotifyPropertyChanged
{
    ObservableCollection<PaymentAttempt> Attempts { get; }

    decimal TotalDueUsd { get; }
    decimal TotalPaidUsd { get; }
    decimal RemainingDueUsd { get; }
    decimal ExchangeRate { get; }

    bool IsFullyPaid { get; }
    bool HasAcceptedAnyPayment { get; }

    CashAcceptorState CashAcceptorState { get; }
    string? CurrentCashStatusMessage { get; }

    event EventHandler<CashAcceptorStateChangedEventArgs>? CashAcceptorStateChanged;
    event EventHandler<NoteInsertedEventArgs>? CashNoteInserted;
    event EventHandler<CashEscrowResolvedEventArgs>? CashEscrowResolved;
    event EventHandler<CashRecyclerJamEventArgs>? CashJamReported;
    event EventHandler<HardwareFaultEventArgs>? CashFaultReported;

    void BeginTransaction(decimal totalDueUsd, decimal exchangeRate, bool isCash = true);
    bool TrySubmitCash(decimal amount, bool isUsd, out string reason);

    Payment? ConfirmPayment(PaymentMethod method = PaymentMethod.Cash);

    void ResetTransaction();
    void AttachCashRecycler(ICashRecycler? cashRecycler);
}