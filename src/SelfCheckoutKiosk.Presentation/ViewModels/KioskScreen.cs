namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>Presentation-layer screens the shell navigates between — a
/// coarser projection of <see cref="SelfCheckoutKiosk.Domain.Enums.KioskState"/>
/// (e.g. both cash sub-states show the same Ingestion screen).</summary>
public enum KioskScreen
{
    Idle,
    Cart,
    Payment,
    Ingestion,
    Success,
    Faulted,
}
