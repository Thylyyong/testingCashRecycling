using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

public sealed class KioskStateChangedEventArgs(KioskState previous, KioskState current) : EventArgs
{
    public KioskState Previous { get; } = previous;
    public KioskState Current { get; } = current;
}

public sealed class ProductAddedEventArgs(Product product, decimal runningTotalUsd) : EventArgs
{
    public Product Product { get; } = product;
    public decimal RunningTotalUsd { get; } = runningTotalUsd;
}

public sealed class BalanceChangedEventArgs(decimal totalUsd, decimal tenderedUsd, decimal remainingUsd) : EventArgs
{
    public decimal TotalUsd { get; } = totalUsd;
    public decimal TenderedUsd { get; } = tenderedUsd;
    public decimal RemainingUsd { get; } = remainingUsd;
}

/// <summary>Result of routing + handling a single raw scan.</summary>
public sealed record ScanResult(ScanCategory Category, bool Accepted, string? Message = null);
