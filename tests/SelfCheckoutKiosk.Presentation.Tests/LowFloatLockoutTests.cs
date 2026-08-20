using SelfCheckoutKiosk.Presentation.ViewModels;
using Xunit;

namespace SelfCheckoutKiosk.Presentation.Tests;

public sealed class LowFloatLockoutTests
{
    [Fact]
    public void IngestionProgress_LowFloatTriggeredThenCleared_TogglesIsLowFloatLocked()
    {
        var engine = new FakeLLCoreLogicEngine();
        var vm = new IngestionProgressViewModel(engine, new InlineUiDispatcher());

        Assert.False(vm.IsLowFloatLocked);

        engine.RaiseLowFloatTriggered();
        Assert.True(vm.IsLowFloatLocked);

        engine.RaiseLowFloatCleared();
        Assert.False(vm.IsLowFloatLocked);
    }

    [Fact]
    public void PaymentSelection_LowFloatTriggered_DisablesCashAndSetsNotice()
    {
        var engine = new FakeLLCoreLogicEngine();
        var vm = new PaymentSelectionViewModel(engine, new InlineUiDispatcher());

        Assert.True(vm.IsCashAvailable);
        Assert.Null(vm.LowFloatNoticeMessage);

        engine.RaiseLowFloatTriggered();

        Assert.False(vm.IsCashAvailable);
        Assert.Equal("Exact Cash / Digital Payments Only", vm.LowFloatNoticeMessage);
    }

    [Fact]
    public void PaymentSelection_LowFloatCleared_ReEnablesCash()
    {
        var engine = new FakeLLCoreLogicEngine();
        var vm = new PaymentSelectionViewModel(engine, new InlineUiDispatcher());

        engine.RaiseLowFloatTriggered();
        engine.RaiseLowFloatCleared();

        Assert.True(vm.IsCashAvailable);
        Assert.Null(vm.LowFloatNoticeMessage);
    }
}
