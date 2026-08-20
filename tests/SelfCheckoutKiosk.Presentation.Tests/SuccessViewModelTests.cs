using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Presentation.ViewModels;
using Xunit;

namespace SelfCheckoutKiosk.Presentation.Tests;

public sealed class SuccessViewModelTests
{
    [Fact]
    public void ArrivingAtSuccess_ExposesLastChangeBreakdown()
    {
        ChangeBreakdown breakdown = ChangeBreakdown.Empty;
        var engine = new FakeLLCoreLogicEngine { LastChangeBreakdown = breakdown };
        using var vm = new SuccessViewModel(engine, new InlineUiDispatcher(), resetDelay: TimeSpan.FromMinutes(5));

        engine.RaiseStateChanged(KioskState.AwaitingPayment, KioskState.TransactionComplete);

        Assert.Same(breakdown, vm.ChangeBreakdown);
    }

    [Fact]
    public async Task ResetTimer_FiresAfterDelay_CallsResetToIdle()
    {
        var engine = new FakeLLCoreLogicEngine();
        using var vm = new SuccessViewModel(engine, new InlineUiDispatcher(), resetDelay: TimeSpan.FromMilliseconds(20));

        engine.RaiseStateChanged(KioskState.AwaitingPayment, KioskState.TransactionComplete);

        await Task.Delay(200);

        Assert.Equal(1, engine.ResetToIdleCallCount);
    }

    [Fact]
    public async Task DoneTappedEarly_CancelsTimer_ResetToIdleCalledOnlyOnce()
    {
        var engine = new FakeLLCoreLogicEngine();
        using var vm = new SuccessViewModel(engine, new InlineUiDispatcher(), resetDelay: TimeSpan.FromMilliseconds(30));

        engine.RaiseStateChanged(KioskState.AwaitingPayment, KioskState.TransactionComplete);

        await vm.StartNewTransactionAsync();
        await Task.Delay(150);

        Assert.Equal(1, engine.ResetToIdleCallCount);
    }
}
