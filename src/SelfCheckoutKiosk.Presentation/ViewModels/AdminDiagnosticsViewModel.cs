using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>Live physical note count for one KHR/USD denomination cassette.</summary>
public sealed class CassetteCountEntry(int denominationKhr, int count)
{
    public int DenominationKhr { get; } = denominationKhr;
    public int Count { get; } = count;
}

/// <summary>
/// Admin Diagnostics — a security-gated screen, so unlike every other
/// ViewModel in this project it reaches past <see cref="ILLCoreLogicEngine"/>
/// into the Core-level HAL/licensing abstractions directly (<see cref="ICashRecycler"/>,
/// <see cref="HardwareAppendLog"/>, <see cref="OfflineLicenseManager"/>) to
/// surface diagnostics the engine's day-to-day surface deliberately doesn't
/// expose. Still only Core types — never Infrastructure or a concrete
/// Hal.Vendor.* adapter — so the project's hard architectural boundary holds.
///
/// The entry gate (<see cref="TryUnlock"/>) is local-only: a fixed technician
/// PIN or a reserved technician barcode string, both compared in constant
/// time to avoid leaking a timing side-channel. Nothing here is exposed
/// until <see cref="IsUnlocked"/> is true.
/// </summary>
public sealed class AdminDiagnosticsViewModel : KioskViewModelBase
{
    private readonly ICashRecycler _cashRecycler;
    private readonly HardwareAppendLog _hardwareAppendLog;
    private readonly OfflineLicenseManager _licenseManager;
    private readonly byte[] _expectedPinUtf8;
    private readonly byte[] _expectedTechnicianBarcodeUtf8;

    public AdminDiagnosticsViewModel(
        ILLCoreLogicEngine engine,
        ICashRecycler cashRecycler,
        HardwareAppendLog hardwareAppendLog,
        OfflineLicenseManager licenseManager,
        IUiDispatcher dispatcher,
        string technicianPin,
        string technicianBarcode)
        : base(engine, dispatcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(technicianPin);
        ArgumentException.ThrowIfNullOrWhiteSpace(technicianBarcode);

        _cashRecycler = cashRecycler;
        _hardwareAppendLog = hardwareAppendLog;
        _licenseManager = licenseManager;
        _expectedPinUtf8 = Encoding.UTF8.GetBytes(technicianPin);
        _expectedTechnicianBarcodeUtf8 = Encoding.UTF8.GetBytes(technicianBarcode);

        _cashRecycler.OnCassetteInventoryChanged += (_, e) => Dispatcher.Post(() => ApplyCassetteCounts(e.CountsByKhrDenomination));
        _cashRecycler.OnFault += (_, e) => Dispatcher.Post(() => RecordFault("CashRecycler", e.Message));
        Engine.OnHardwareFault += (_, e) => Dispatcher.Post(() => RecordFault(e.Device, e.Message));
    }

    // ---- entry gate ---------------------------------------------------

    private bool _isUnlocked;
    public bool IsUnlocked
    {
        get => _isUnlocked;
        private set => SetProperty(ref _isUnlocked, value);
    }

    /// <summary>Validates a scanned/typed credential against the fixed PIN or
    /// the reserved technician barcode string. Returns false without
    /// distinguishing which check failed — don't leak that detail to the UI.</summary>
    public bool TryUnlock(string enteredCredential)
    {
        byte[] entered = Encoding.UTF8.GetBytes(enteredCredential ?? string.Empty);

        bool matchesPin = FixedTimeEquals(entered, _expectedPinUtf8);
        bool matchesBarcode = FixedTimeEquals(entered, _expectedTechnicianBarcodeUtf8);

        if (matchesPin || matchesBarcode)
        {
            IsUnlocked = true;
            RefreshSnapshot();
            return true;
        }

        return false;
    }

    public void Lock() => IsUnlocked = false;

    private static bool FixedTimeEquals(byte[] a, byte[] b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);

    // ---- diagnostics surface --------------------------------------------

    public ObservableCollection<CassetteCountEntry> CassetteCounts { get; } = [];

    public ObservableCollection<string> DeviceFaults { get; } = [];

    private bool _hasUnresolvedEscrowOrphan;
    public bool HasUnresolvedEscrowOrphan
    {
        get => _hasUnresolvedEscrowOrphan;
        private set => SetProperty(ref _hasUnresolvedEscrowOrphan, value);
    }

    private LicenseTier _licenseTier;
    public LicenseTier LicenseTier
    {
        get => _licenseTier;
        private set => SetProperty(ref _licenseTier, value);
    }

    private bool _isLicenseValidated;
    public bool IsLicenseValidated
    {
        get => _isLicenseValidated;
        private set => SetProperty(ref _isLicenseValidated, value);
    }

    /// <summary>Re-reads the point-in-time diagnostics that have no
    /// dedicated change event (orphan flag, license status). Call on unlock
    /// and whenever the technician hits "refresh".</summary>
    public void RefreshSnapshot()
    {
        Dispatcher.Post(() =>
        {
            HasUnresolvedEscrowOrphan = _hardwareAppendLog.HasUnresolvedEscrowEntry;
            IsLicenseValidated = _licenseManager.IsValidated;
            LicenseTier = _licenseManager.Tier;
        });
    }

    private void ApplyCassetteCounts(IReadOnlyDictionary<int, int> countsByDenomination)
    {
        CassetteCounts.Clear();
        foreach ((int denomination, int count) in countsByDenomination.OrderBy(kvp => kvp.Key))
            CassetteCounts.Add(new CassetteCountEntry(denomination, count));
    }

    private void RecordFault(string device, string message) =>
        DeviceFaults.Insert(0, $"{DateTimeOffset.Now:T} — {device}: {message}");
}
