using SelfCheckoutKiosk.Domain.Entities;
using System.Linq;
using Xunit;

namespace SelfCheckoutKiosk.Presentation.Tests;

public sealed class CartViewModelTests
{
    private static Product MakeProduct(string ean13, string description, decimal usdPrice) => new()
    {
        Ean13 = ean13,
        Description = description,
        UsdPrice = usdPrice,
    };

    [Fact]
    public void ProductAdded_NewEan_AddsLineItem()
    {
        var engine = new FakeLLCoreLogicEngine();
        var vm = new ViewModels.CartViewModel(engine, new InlineUiDispatcher());

        engine.RaiseProductAdded(MakeProduct("111", "Widget", 2.5m), 2.5m);

        Assert.Single(vm.Items);
        Assert.Equal("111", vm.Items[0].Ean13);
        Assert.Equal(1, vm.Items[0].Quantity);
        Assert.True(vm.HasItems);
    }

    [Fact]
    public void ProductAdded_RepeatEan_AccumulatesQuantityInsteadOfNewLine()
    {
        var engine = new FakeLLCoreLogicEngine();
        var vm = new ViewModels.CartViewModel(engine, new InlineUiDispatcher());

        engine.RaiseProductAdded(MakeProduct("111", "Widget", 2.5m), 2.5m);
        engine.RaiseProductAdded(MakeProduct("111", "Widget", 2.5m), 5.0m);
        engine.RaiseProductAdded(MakeProduct("222", "Gadget", 1.0m), 6.0m);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(2, vm.Items.Single(i => i.Ean13 == "111").Quantity);
        Assert.Equal(1, vm.Items.Single(i => i.Ean13 == "222").Quantity);
    }

    [Fact]
    public void BalanceChanged_UpdatesRunningTotal()
    {
        var engine = new FakeLLCoreLogicEngine { UsdToKhrRate = 4000m };
        var vm = new ViewModels.CartViewModel(engine, new InlineUiDispatcher());

        engine.RaiseBalanceChanged(totalUsd: 9.5m, tenderedUsd: 0m, remainingUsd: 9.5m);

        Assert.Equal(9.5m, vm.RunningTotalUsd);
        Assert.Equal(38_000m, vm.RunningTotalKhr);
    }
}
