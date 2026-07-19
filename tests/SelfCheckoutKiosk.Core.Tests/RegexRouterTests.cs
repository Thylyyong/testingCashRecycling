using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class RegexRouterTests
{
    [Theory]
    [InlineData("4006381333931", ScanCategory.Ean13Product)]
    [InlineData("VCH-SUMMER2026", ScanCategory.OfflineCoupon)]
    [InlineData("00020101021229", ScanCategory.KhqrProfile)]
    [InlineData("hello", ScanCategory.Unknown)]
    public void Classify_ReturnsExpectedCategory(string raw, ScanCategory expected)
        => Assert.Equal(expected, RegexRouter.Classify(raw));
}
