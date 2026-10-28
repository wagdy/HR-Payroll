using HrPayroll.Domain.Common;
using HrPayroll.Domain.Custody;

namespace HrPayroll.Domain.Tests;

// Consumables inventory (جرد الاستهلاكيات): supplies kept in the store and used up, their stock and
// what a unit cost on average, moved by stock received, used up and counted.
public class ConsumablesTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static ConsumableItem Napkins(decimal? reorderLevel = 20) => new(Guid.NewGuid(), " Napkins ", " pack ", reorderLevel, "Omar");

    [Fact]
    public void An_item_is_named_counted_in_its_unit_and_empty_until_stock_comes_in()
    {
        var napkins = Napkins();
        Assert.Equal(("Napkins", "pack", 0m, 0m, 0m), (napkins.Name, napkins.Unit, napkins.Quantity, napkins.UnitCost, napkins.TotalCost));
        // Nothing in stock, at or under its level: low.
        Assert.True(napkins.IsLow);
        Assert.False(Napkins(reorderLevel: null).IsLow);

        Assert.Throws<DomainException>(() => napkins.Rename(" "));
        Assert.Throws<DomainException>(() => napkins.SetUnit(""));
        Assert.Throws<DomainException>(() => napkins.SetUnit(new string('x', ConsumableItem.MaxUnitLength + 1)));
        Assert.Throws<DomainException>(() => napkins.SetReorderLevel(-1));
    }

    [Fact]
    public void Stock_received_moves_the_average_cost_and_what_is_used_up_goes_at_it()
    {
        var napkins = Napkins();
        var first = napkins.Receive(Guid.NewGuid(), 100, 2.5m, Monday, " From the market ", "Omar");
        Assert.Equal((ConsumableMovementType.Received, 100m, 2.5m, 250m, 100m, "From the market"),
            (first.Type, first.Quantity, first.UnitCost, first.Cost, first.StockAfter, first.Notes));

        // 100 at 2.50 and 50 at 4.00: 450 for 150, 3.00 each.
        napkins.Receive(Guid.NewGuid(), 50, 4m, Monday, null, "Omar");
        Assert.Equal((150m, 3m, 450m), (napkins.Quantity, napkins.UnitCost, napkins.TotalCost));

        var used = napkins.Consume(Guid.NewGuid(), 30, Monday.AddDays(1), "The hall", "Mona");
        Assert.Equal((ConsumableMovementType.Consumed, 30m, 3m, 90m, 120m), (used.Type, used.Quantity, used.UnitCost, used.Cost, used.StockAfter));
        Assert.Equal((120m, 360m), (napkins.Quantity, napkins.TotalCost));

        // No price given (an employee is never shown one): at the average, which stays.
        napkins.Receive(Guid.NewGuid(), 30, null, Monday, null, "Hassan");
        Assert.Equal((150m, 3m), (napkins.Quantity, napkins.UnitCost));

        var refused = Assert.Throws<DomainException>(() => napkins.Consume(Guid.NewGuid(), 151, Monday, null, "Mona"));
        Assert.Equal("There are only 150 pack in stock.", refused.Message);
        Assert.Throws<DomainException>(() => napkins.Consume(Guid.NewGuid(), 0, Monday, null, "Mona"));
        Assert.Throws<DomainException>(() => napkins.Receive(Guid.NewGuid(), 10, -1, Monday, null, "Omar"));
        Assert.Equal(150m, napkins.Quantity);
    }

    [Fact]
    public void A_count_sets_the_stock_to_what_was_counted_and_keeps_the_difference()
    {
        var detergent = new ConsumableItem(Guid.NewGuid(), "Detergent", "litre", 5, "Omar");
        detergent.Receive(Guid.NewGuid(), 12.5m, 40, Monday, null, "Omar");

        var short_ = detergent.Count(Guid.NewGuid(), 10.25m, Monday.AddDays(7), "Monthly count", "Omar");
        Assert.Equal((ConsumableMovementType.Counted, -2.25m, 40m, 90m, 10.25m), (short_.Type, short_.Quantity, short_.UnitCost, short_.Cost, short_.StockAfter));
        Assert.Equal((10.25m, 410m), (detergent.Quantity, detergent.TotalCost));

        // Found more, or just what there was: the difference, or none.
        Assert.Equal(1m, detergent.Count(Guid.NewGuid(), 11.25m, Monday.AddDays(8), null, "Omar").Quantity);
        Assert.Equal(0m, detergent.Count(Guid.NewGuid(), 11.25m, Monday.AddDays(9), null, "Omar").Quantity);

        // Low at or under its level.
        Assert.False(detergent.IsLow);
        detergent.Count(Guid.NewGuid(), 5, Monday.AddDays(10), null, "Omar");
        Assert.True(detergent.IsLow);

        // To the thousandth, never below zero.
        Assert.Throws<DomainException>(() => detergent.Count(Guid.NewGuid(), 1.2345m, Monday, null, "Omar"));
        Assert.Throws<DomainException>(() => detergent.Count(Guid.NewGuid(), -1, Monday, null, "Omar"));
    }
}
