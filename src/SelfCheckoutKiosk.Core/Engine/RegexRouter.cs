using System.Text.RegularExpressions;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Engine;

/// <summary>
/// Classifies every raw scan as EAN-13 product / KHQR profile / VCH- offline
/// coupon before the engine dispatches it (Blueprint §3). Source-generated
/// regexes are AOT-friendly (no runtime regex compilation).
///
/// TODO(Back-End): confirm the exact KHQR/coupon patterns and add unit tests
/// in SelfCheckoutKiosk.Core.Tests. Ordering matters — most specific first.
/// </summary>
public static partial class RegexRouter
{
    [GeneratedRegex(@"^\d{13}$")]
    private static partial Regex Ean13();

    [GeneratedRegex(@"^VCH-[A-Z0-9]+$")]
    private static partial Regex Coupon();

    [GeneratedRegex(@"^000201")] // KHQR payloads begin with EMVCo tag "0002"+"01"
    private static partial Regex Khqr();

    public static ScanCategory Classify(string rawScan)
    {
        ArgumentNullException.ThrowIfNull(rawScan);
        if (Coupon().IsMatch(rawScan)) return ScanCategory.OfflineCoupon;
        if (Khqr().IsMatch(rawScan)) return ScanCategory.KhqrProfile;
        if (Ean13().IsMatch(rawScan)) return ScanCategory.Ean13Product;
        return ScanCategory.Unknown;
    }
}
