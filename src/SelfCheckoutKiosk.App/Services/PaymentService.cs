using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App.Diagnostics;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Infrastructure.Data;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Connected PaymentService managing exact cash, QR payments, hardware inhibition,
/// live balance calculation, change dispensing, and database transaction commits.
/// Fully wired to ICashRecycler and ICashEscrowController states and lifecycle events.
/// </summary>
public class PaymentService : IPaymentService
{
    public LocalizationService Localizer => LocalizationService.Instance;
    private const decimal MaxOverpayKhr = 100m;

    private readonly Func<KioskDbContext>? _dbContextFactory;
    private ICashRecycler? _cashRecycler;
    private readonly HardwareAppendLog? _hardwareLog;
    private readonly CashAcceptancePolicy _acceptancePolicy;

    private decimal _totalDueUsd;
    private decimal _totalPaidUsd;
    private decimal _exchangeRate = 4100m;
    private CashAcceptorState _cashAcceptorState = CashAcceptorState.Inactive;
    private string? _currentCashStatusMessage;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<CashAcceptorStateChangedEventArgs>? CashAcceptorStateChanged;
    public event EventHandler<NoteInsertedEventArgs>? CashNoteInserted;
    public event EventHandler<CashEscrowResolvedEventArgs>? CashEscrowResolved;
    public event EventHandler<CashRecyclerJamEventArgs>? CashJamReported;
    public event EventHandler<HardwareFaultEventArgs>? CashFaultReported;

    public ObservableCollection<PaymentAttempt> Attempts { get; } = new();

    public decimal TotalDueUsd
    {
        get => _totalDueUsd;
        private set
        {
            if (_totalDueUsd == value) return;
            _totalDueUsd = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RemainingDueUsd));
            OnPropertyChanged(nameof(IsFullyPaid));
        }
    }

    public decimal TotalPaidUsd
    {
        get => _totalPaidUsd;
        private set
        {
            if (_totalPaidUsd == value) return;
            _totalPaidUsd = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RemainingDueUsd));
            OnPropertyChanged(nameof(IsFullyPaid));
        }
    }

    public decimal ExchangeRate
    {
        get => _exchangeRate;
        private set
        {
            if (_exchangeRate == value) return;
            _exchangeRate = value;
            OnPropertyChanged();
        }
    }

    public decimal RemainingDueUsd => Math.Max(0, TotalDueUsd - TotalPaidUsd);

    public bool IsFullyPaid => TotalDueUsd > 0 && TotalPaidUsd >= TotalDueUsd;

    public bool HasAcceptedAnyPayment { get; private set; }

    public CashAcceptorState CashAcceptorState
    {
        get => _cashAcceptorState;
        private set
        {
            if (_cashAcceptorState == value) return;
            _cashAcceptorState = value;
            OnPropertyChanged();
        }
    }

    public string? CurrentCashStatusMessage
    {
        get => _currentCashStatusMessage;
        private set
        {
            if (_currentCashStatusMessage == value) return;
            _currentCashStatusMessage = value;
            OnPropertyChanged();
        }
    }

    public PaymentService(
        Func<KioskDbContext>? dbContextFactory = null,
        ICashRecycler? cashRecycler = null,
        HardwareAppendLog? hardwareLog = null)
    {
        _dbContextFactory = dbContextFactory;
        _hardwareLog = hardwareLog;
        _acceptancePolicy = new CashAcceptancePolicy();

        AttachCashRecycler(cashRecycler);
    }

    public void AttachCashRecycler(ICashRecycler? cashRecycler)
    {
        if (_cashRecycler != null)
        {
            _cashRecycler.OnNoteInserted -= HandlePhysicalNoteInserted;
            _cashRecycler.OnNoteInEscrow -= HandlePhysicalNoteInEscrow;
            _cashRecycler.OnAcceptorStateChanged -= HandlePhysicalAcceptorStateChanged;
            _cashRecycler.OnJam -= HandlePhysicalJam;
            _cashRecycler.OnFault -= HandlePhysicalFault;

            if (_cashRecycler is ICashEscrowController oldEscrow)
            {
                oldEscrow.OnEscrowResolved -= HandlePhysicalEscrowResolved;
            }
        }

        _cashRecycler = cashRecycler;

        if (_cashRecycler != null)
        {
            _cashRecycler.OnNoteInserted += HandlePhysicalNoteInserted;
            _cashRecycler.OnNoteInEscrow += HandlePhysicalNoteInEscrow;
            _cashRecycler.OnAcceptorStateChanged += HandlePhysicalAcceptorStateChanged;
            _cashRecycler.OnJam += HandlePhysicalJam;
            _cashRecycler.OnFault += HandlePhysicalFault;

            if (_cashRecycler is ICashEscrowController newEscrow)
            {
                newEscrow.OnEscrowResolved += HandlePhysicalEscrowResolved;
            }

            DiagnosticLogger.Log($"[PaymentService] Cash recycler attached ({_cashRecycler.GetType().Name}).");
        }
    }

    public void BeginTransaction(decimal totalDueUsd, decimal exchangeRate)
    {
        ExchangeRate = exchangeRate;
        TotalDueUsd = totalDueUsd;
        TotalPaidUsd = 0;
        HasAcceptedAnyPayment = false;
        CurrentCashStatusMessage = null;
        Attempts.Clear();
        OnPropertyChanged(nameof(HasAcceptedAnyPayment));

        RecordAttempt(PaymentAttemptResult.Info, "Cash payment ready — please insert USD ($) or KHR (៛) banknotes.", 0, isUsd: true);

        // Hardware Inhibit Rule: Arm acceptor only when entering cash session
        if (_cashRecycler != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    DiagnosticLogger.Log("[PaymentService] Arming cash acceptor (Enabling slot LED)...");
                    await _cashRecycler.ArmAcceptanceAsync();
                    DiagnosticLogger.Log("[PaymentService] Cash acceptor successfully armed (Slot Ready).");
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogError($"[PaymentService] ArmAcceptanceAsync error: {ex.Message}", ex);
                }
            });
        }
    }

    public bool TrySubmitCash(decimal amount, bool isUsd, out string reason)
    {
        if (IsFullyPaid)
        {
            reason = Localizer.GetString("PaymentAlreadyComplete");
            RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);
            return false;
        }

        if (amount <= 0)
        {
            reason = Localizer.GetString("InvalidAmount");
            RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);
            return false;
        }

        decimal amountUsd = isUsd ? amount : amount / ExchangeRate;
        decimal maxOverpayUsd = MaxOverpayKhr / ExchangeRate;
        decimal projectedTotal = TotalPaidUsd + amountUsd;

        if (projectedTotal > TotalDueUsd + maxOverpayUsd)
        {
            reason = Localizer.GetString("NoteTooLarge");
            RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);

            // Physically reject overpaying note
            if (_cashRecycler != null)
            {
                Task.Run(async () =>
                {
                    try { await _cashRecycler.RejectEscrowedNoteAsync(); } catch { }
                });
            }

            return false;
        }

        // Accept valid cash note
        TotalPaidUsd += amountUsd;
        HasAcceptedAnyPayment = true;
        reason = Localizer.GetString("Accepted");

        RecordAttempt(PaymentAttemptResult.Accepted, reason, amount, isUsd);
        OnPropertyChanged(nameof(HasAcceptedAnyPayment));

        // Log audit event and update vault inventory
        _hardwareLog?.LogCommittedToVault(isUsd ? Money.Usd(amount) : Money.Khr(amount));
        VaultInventoryService.Instance.RecordDeposit(isUsd ? "USD" : "KHR", (int)amount);

        // Hardware Inhibit Rule: Once total is met, immediately disable/inhibit acceptor
        if (IsFullyPaid && _cashRecycler != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _cashRecycler.DisarmAcceptanceAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PaymentService] DisarmAcceptanceAsync error: {ex.Message}");
                }
            });
        }

        return true;
    }

    private void HandlePhysicalNoteInserted(object? sender, NoteInsertedEventArgs e)
    {
        DiagnosticLogger.Log($"[PaymentService] Banknote inserted into validator slot: {e.Note.Amount} {e.Note.Currency}");
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            CurrentCashStatusMessage = "Reading banknote...";
            CashNoteInserted?.Invoke(this, e);
        });
    }

    private void HandlePhysicalNoteInEscrow(object? sender, NoteInEscrowEventArgs e)
    {
        DiagnosticLogger.Log($"[PaymentService] Note in escrow: {e.Note.Amount} {e.Note.Currency}");
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(async () =>
        {
            if (IsFullyPaid)
            {
                DiagnosticLogger.Log($"[PaymentService] Already fully paid. Rejecting note {e.Note.Amount} {e.Note.Currency}.");
                if (_cashRecycler != null)
                {
                    try { await _cashRecycler.RejectEscrowedNoteAsync(); } catch { }
                }
                RecordAttempt(PaymentAttemptResult.Rejected, "Payment already complete", e.Note.Amount, e.Note.Currency == CurrencyCode.Usd);
                return;
            }

            if (_cashRecycler is ICashEscrowController escrowController)
            {
                try
                {
                    var targetKhr = Money.Khr(Math.Max(1m, Math.Round(TotalDueUsd * ExchangeRate, MidpointRounding.AwayFromZero)));
                    var currentlyPaidKhr = Money.Khr(Math.Round(TotalPaidUsd * ExchangeRate, MidpointRounding.AwayFromZero));

                    var decision = _acceptancePolicy.Evaluate(targetKhr, currentlyPaidKhr, e.Note, ExchangeRate, MaxOverpayKhr);

                    if (decision.ShouldAcceptNote)
                    {
                        DiagnosticLogger.Log($"[PaymentService] Acceptance policy ACCEPTED note {e.Note.Amount} {e.Note.Currency} - Committing to vault.");
                        CurrentCashStatusMessage = "Accepting banknote...";
                        await escrowController.CommitEscrowedNoteAsync();
                    }
                    else
                    {
                        DiagnosticLogger.Log($"[PaymentService] Acceptance policy REJECTED note {e.Note.Amount} {e.Note.Currency} - Returning from escrow. Decision: {decision.Decision}");
                        
                        bool isUsdNote = e.Note.Currency == CurrencyCode.Usd;
                        string formattedNote = isUsdNote ? $"${e.Note.Amount:0.00} USD" : $"៛{e.Note.Amount:N0} KHR";
                        string rejectReason = decision.Decision switch
                        {
                            CashAcceptanceDecision.RejectOverpayment =>
                                $"Returned — {formattedNote} exceeds remaining balance (${RemainingDueUsd:0.00})",
                            _ => $"Returned — {formattedNote} (Not accepted)"
                        };

                        RecordAttempt(PaymentAttemptResult.Rejected, rejectReason, e.Note.Amount, isUsdNote);
                        CurrentCashStatusMessage = $"Returning {formattedNote}. Please take banknote from slot.";

                        await _cashRecycler.RejectEscrowedNoteAsync();
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogError($"[PaymentService] Note evaluation error: {ex.Message}", ex);
                    try { await _cashRecycler.RejectEscrowedNoteAsync(); } catch { }
                }
            }
            else
            {
                bool isUsd = e.Note.Currency == CurrencyCode.Usd;
                TrySubmitCash(e.Note.Amount, isUsd, out _);
            }
        });
    }

    private void HandlePhysicalEscrowResolved(object? sender, CashEscrowResolvedEventArgs e)
    {
        DiagnosticLogger.Log($"[PaymentService] Escrow resolved: {e.Resolution} for note {e.Note.Amount} {e.Note.Currency}");
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            CurrentCashStatusMessage = null;

            bool isUsd = e.Note.Currency == CurrencyCode.Usd;
            decimal amount = e.Note.Amount;

            if (e.Resolution == CashEscrowResolution.CommittedToVault || e.Resolution == CashEscrowResolution.Committed)
            {
                decimal amountUsd = isUsd ? amount : amount / ExchangeRate;
                TotalPaidUsd += amountUsd;
                HasAcceptedAnyPayment = true;
                OnPropertyChanged(nameof(HasAcceptedAnyPayment));

                string acceptedReason = isUsd
                    ? $"Accepted — ${amount:0.00} USD added"
                    : $"Accepted — ៛{amount:N0} KHR added";

                RecordAttempt(PaymentAttemptResult.Accepted, acceptedReason, amount, isUsd);
                _hardwareLog?.LogCommittedToVault(e.Note);
                VaultInventoryService.Instance.RecordDeposit(isUsd ? "USD" : "KHR", (int)amount);

                DiagnosticLogger.Log($"[PaymentService] Banknote ACCEPTED & committed: {amount} {(isUsd ? "USD" : "KHR")}. Total Paid: ${TotalPaidUsd:0.00} / ${TotalDueUsd:0.00}");

                if (IsFullyPaid && _cashRecycler != null)
                {
                    Task.Run(async () =>
                    {
                        try { await _cashRecycler.DisarmAcceptanceAsync(); } catch { }
                    });
                }
            }
            else
            {
                if (amount <= 0)
                {
                    string rejectReason = "Banknote rejected — Unrecognized or invalid bill. Please take bill from slot.";
                    RecordAttempt(PaymentAttemptResult.Rejected, rejectReason, 0, isUsd: true);
                    DiagnosticLogger.Log($"[PaymentService] Hardware rejected unrecognized bill.");
                }
                CurrentCashStatusMessage = "Please take your returned banknote from the slot.";
            }

            CashEscrowResolved?.Invoke(this, e);
        });
    }

    private void HandlePhysicalAcceptorStateChanged(object? sender, CashAcceptorStateChangedEventArgs e)
    {
        DiagnosticLogger.Log($"[PaymentService] Cash acceptor state changed: {e.State}");
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            CashAcceptorState = e.State;
            bool isFaulted = e.State == CashAcceptorState.Error;
            HardwareStatusManager.Instance.SetCashAvailability(
                !isFaulted,
                e.State == CashAcceptorState.Ready ? "Ready" : e.State.ToString()
            );

            if (e.State == CashAcceptorState.Ready)
            {
                RecordAttempt(PaymentAttemptResult.Info, "Cash acceptor is ready. Please insert banknotes.", 0, isUsd: true);
            }

            CashAcceptorStateChanged?.Invoke(this, e);
        });
    }

    private void HandlePhysicalJam(object? sender, CashRecyclerJamEventArgs e)
    {
        DiagnosticLogger.LogError($"[PaymentService] Cash recycler jam reported: {e.Message}");
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            HardwareStatusManager.Instance.SetCashAvailability(false, $"Jammed: {e.Message}");
            RecordAttempt(PaymentAttemptResult.Warning, $"Mechanism warning: {e.Message}", 0, isUsd: true);
            CashJamReported?.Invoke(this, e);
        });
    }

    private void HandlePhysicalFault(object? sender, HardwareFaultEventArgs e)
    {
        DiagnosticLogger.LogError($"[PaymentService] Cash recycler fault reported: {e.Message}");
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            HardwareStatusManager.Instance.SetCashAvailability(false, $"Fault: {e.Message}");
            RecordAttempt(PaymentAttemptResult.Error, $"Cash device fault: {e.Message}", 0, isUsd: true);
            CashFaultReported?.Invoke(this, e);
        });
    }

    public Models.Payment? ConfirmPayment(Models.PaymentMethod method = Models.PaymentMethod.Cash)
    {
        decimal due = TotalDueUsd > 0 ? TotalDueUsd : 1.00m;
        decimal tenderedUsd = method == Models.PaymentMethod.Cash ? (TotalPaidUsd > 0 ? TotalPaidUsd : due) : due;
        decimal overpaymentUsd = Math.Max(0, tenderedUsd - due);
        decimal changeKhr = Math.Floor(overpaymentUsd * ExchangeRate);

        // Hardware Inhibit Rule: Disarm bill acceptor on confirmation
        if (_cashRecycler != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _cashRecycler.DisarmAcceptanceAsync();
                    if (overpaymentUsd > 0)
                    {
                        var changeBreakdown = new ChangeBreakdown(
                            Array.Empty<KeyValuePair<Money, int>>(),
                            new[] { new KeyValuePair<Money, int>(Money.Khr(changeKhr), 1) }
                        );
                        try { await _cashRecycler.DispenseAsync(changeBreakdown); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PaymentService] Cash device completion error: {ex.Message}");
                }
            });
        }

        var payment = new Models.Payment
        {
            Method = method,
            TotalDueUsd = due,
            TotalPaidUsd = tenderedUsd,
            ChangeDueUsd = overpaymentUsd,
            ExchangeRate = ExchangeRate,
            IsFullyPaid = true,
            CompletedAt = DateTime.Now
        };

        // Persist transaction to local SQLite database with SyncStatus.Pending
        if (_dbContextFactory != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    using var db = _dbContextFactory();
                    var dbTx = new Domain.Entities.Transaction
                    {
                        TransactionGuid = Guid.NewGuid(),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        TotalUsd = TotalDueUsd,
                        TenderedUsd = tenderedUsd,
                        PaymentMethod = method == Models.PaymentMethod.Cash ? Domain.Enums.PaymentMethod.Cash : Domain.Enums.PaymentMethod.KhqrDigital,
                        SyncStatus = SyncStatus.Pending
                    };

                    await db.Transactions.AddAsync(dbTx);
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PaymentService] DB Commit warning: {ex.Message}");
                }
            });
        }

        return payment;
    }

    public void ResetTransaction()
    {
        // Hardware Inhibit Rule: Disarm bill acceptor and return any note in escrow when cancelled or timed out
        if (_cashRecycler != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _cashRecycler.RejectEscrowedNoteAsync();
                }
                catch { }

                try
                {
                    await _cashRecycler.DisarmAcceptanceAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PaymentService] Disarm error on reset: {ex.Message}");
                }
            });
        }

        TotalDueUsd = 0;
        TotalPaidUsd = 0;
        HasAcceptedAnyPayment = false;
        CurrentCashStatusMessage = null;
        Attempts.Clear();
        OnPropertyChanged(nameof(HasAcceptedAnyPayment));
    }

    private void RecordAttempt(PaymentAttemptResult result, string reason, decimal amount, bool isUsd)
    {
        Attempts.Insert(0, new PaymentAttempt
        {
            Amount = amount,
            IsUsd = isUsd,
            Result = result,
            Reason = reason,
            Timestamp = DateTime.Now
        });
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}