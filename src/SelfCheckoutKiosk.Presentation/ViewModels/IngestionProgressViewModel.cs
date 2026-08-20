using System.Collections.ObjectModel;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>One resolved escrow note, oldest first — the note-by-note
/// ingestion feedback list.</summary>
public sealed class EscrowEntry(Money note, bool accepted)
{
    public Money Note { get; } = note;
    public bool Accepted { get; } = accepted;
}

/// <summary>
/// IngestionProgressView — live cash-tender feedback while notes are fed into
/// the recycler. Populated entirely from <see cref="ILLCoreLogicEngine.OnNoteProcessed"/>
/// and <see cref="ILLCoreLogicEngine.OnBalanceChanged"/>, both of which the
/// engine raises from the cash recycler's background polling/callback thread
/// — every property write here is marshaled through <see cref="KioskViewModelBase.Dispatcher"/>.
/// </summary>
public sealed class IngestionProgressViewModel : KioskViewModelBase
{
    public IngestionProgressViewModel(ILLCoreLogicEngine engine, IUiDispatcher dispatcher)
        : base(engine, dispatcher)
    {
        Engine.OnBalanceChanged += HandleBalanceChanged;
        Engine.OnNoteProcessed += HandleNoteProcessed;
        Engine.LowFloatStateTriggered += (_, _) => Dispatcher.Post(() => IsLowFloatLocked = true);
        Engine.LowFloatStateCleared += (_, _) => Dispatcher.Post(() => IsLowFloatLocked = false);
    }

    /// <summary>Oldest-first feed of every note the recycler has resolved
    /// this tender — bind a ListView/ItemsRepeater directly to this.</summary>
    public ObservableCollection<EscrowEntry> EscrowEntries { get; } = [];

    private decimal _totalUsd;
    public decimal TotalUsd
    {
        get => _totalUsd;
        private set => SetProperty(ref _totalUsd, value);
    }

    private decimal _tenderedUsd;
    public decimal TenderedUsd
    {
        get => _tenderedUsd;
        private set => SetProperty(ref _tenderedUsd, value);
    }

    private decimal _remainingUsd;
    public decimal RemainingUsd
    {
        get => _remainingUsd;
        private set => SetProperty(ref _remainingUsd, value);
    }

    private bool _isLowFloatLocked;

    /// <summary>True while the recycler is in exact-cash-only lockout — the
    /// ingestion screen should surface the same notice as <see cref="PaymentSelectionViewModel.LowFloatNoticeMessage"/>.</summary>
    public bool IsLowFloatLocked
    {
        get => _isLowFloatLocked;
        private set => SetProperty(ref _isLowFloatLocked, value);
    }

    /// <summary>0.0–1.0 fill fraction for a ProgressBar; 1.0 once fully tendered.</summary>
    public double ProgressFraction => TotalUsd <= 0m
        ? 0d
        : (double)Math.Clamp(TenderedUsd / TotalUsd, 0m, 1m);

    private void HandleBalanceChanged(object? sender, BalanceChangedEventArgs e)
    {
        Dispatcher.Post(() =>
        {
            TotalUsd = e.TotalUsd;
            TenderedUsd = e.TenderedUsd;
            RemainingUsd = e.RemainingUsd;
            OnPropertyChanged(nameof(ProgressFraction));
        });
    }

    private void HandleNoteProcessed(object? sender, NoteProcessedEventArgs e)
    {
        Dispatcher.Post(() => EscrowEntries.Add(new EscrowEntry(e.Note, e.Accepted)));
    }
}
