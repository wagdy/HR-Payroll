using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Custody;

public enum ConsumableMovementType
{
    // Stock in (وارد): bought and put in the store, at what a unit cost.
    Received = 1,

    // Used up (منصرف): taken from the store.
    Consumed = 2,

    // A count (جرد): the stock set to what was counted; the difference, more or less, recorded.
    Counted = 3,
}

// A consumable supply (صنف مستهلك): napkins, detergent, gloves, takeaway cups; bought, kept in the
// store and used up, never in anyone's custody (that is an asset's, AssetItem). Its stock in its
// unit (a piece, a box, a kilo, a litre), and what a unit cost on average: stock received at its
// price moves the average; stock used up, or found short on a count, goes at it. The stock
// changes only by movements (ConsumableMovement), none deleted. At or under its reorder level it
// is low: time to buy more.
public sealed class ConsumableItem : AggregateRoot
{
    public const int MaxNameLength = 150;
    public const int MaxUnitLength = 20;
    public const int MaxNotesLength = 300;
    public const decimal MaxQuantity = 1_000_000;
    public const decimal MaxUnitCost = 10_000_000;

    private ConsumableItem() { }

    public ConsumableItem(Guid id, string name, string unit, decimal? reorderLevel, string createdBy, string? notes = null)
    {
        Id = id;
        Rename(name);
        SetUnit(unit);
        SetReorderLevel(reorderLevel);
        SetNotes(notes);
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;

    // In the store now, in its unit.
    public decimal Quantity { get; private set; }

    // What a unit cost, on average, of the stock there is (the last one's, with none left).
    public decimal UnitCost { get; private set; }

    // Low at or under it; none, never low.
    public decimal? ReorderLevel { get; private set; }

    public string? Notes { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    // What the stock is worth.
    public decimal TotalCost => Money(Quantity * UnitCost);

    public bool IsLow => ReorderLevel is { } level && Quantity <= level;

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new DomainException("Give the item its name.");
        if (trimmed.Length > MaxNameLength)
            throw new DomainException($"Keep the item's name under {MaxNameLength} characters.");
        Name = trimmed;
    }

    // A piece, a box, a kilo: what its quantities count.
    public void SetUnit(string unit)
    {
        var trimmed = unit?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new DomainException("Say what it is counted in: a piece, a box, a kilo.");
        if (trimmed.Length > MaxUnitLength)
            throw new DomainException($"Keep the unit under {MaxUnitLength} characters.");
        Unit = trimmed;
    }

    public void SetReorderLevel(decimal? level)
    {
        if (level is { } l)
            CheckQuantity(l, allowZero: true);
        ReorderLevel = level;
    }

    public void SetNotes(string? notes)
    {
        var trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmed?.Length > MaxNotesLength)
            throw new DomainException($"Keep the notes under {MaxNotesLength} characters.");
        Notes = trimmed;
    }

    // Stock in (وارد), at what a unit cost; none given, at the average (an employee is never shown
    // what things cost). The average after is the old stock's and the new's together.
    public ConsumableMovement Receive(Guid movementId, decimal quantity, decimal? unitCost, DateOnly date, string? notes, string recordedBy)
    {
        CheckQuantity(quantity, allowZero: false);
        var cost = unitCost is { } given ? Money(given) : UnitCost;
        if (cost is < 0 or > MaxUnitCost)
            throw new DomainException("A unit's cost is zero or more; check the amount.");
        var stock = Quantity + quantity;
        if (stock > MaxQuantity)
            throw new DomainException($"That would be more than {MaxQuantity:N0} in stock; check the quantity.");
        UnitCost = Math.Round((Quantity * UnitCost + quantity * cost) / stock, 4, MidpointRounding.AwayFromZero);
        Quantity = stock;
        return new ConsumableMovement(movementId, Id, ConsumableMovementType.Received, date, quantity, cost, Quantity, notes, recordedBy);
    }

    // Used up (منصرف), at the average; never more than there is.
    public ConsumableMovement Consume(Guid movementId, decimal quantity, DateOnly date, string? notes, string recordedBy)
    {
        CheckQuantity(quantity, allowZero: false);
        if (quantity > Quantity)
            throw new DomainException($"There are only {Quantity:0.###} {Unit} in stock.");
        Quantity -= quantity;
        return new ConsumableMovement(movementId, Id, ConsumableMovementType.Consumed, date, quantity, UnitCost, Quantity, notes, recordedBy);
    }

    // Counted (جرد): the stock is what was counted. The movement holds the difference: more than
    // there was, or less (negative), at the average; none, the count confirms it.
    public ConsumableMovement Count(Guid movementId, decimal counted, DateOnly date, string? notes, string recordedBy)
    {
        CheckQuantity(counted, allowZero: true);
        var difference = counted - Quantity;
        Quantity = counted;
        return new ConsumableMovement(movementId, Id, ConsumableMovementType.Counted, date, difference, UnitCost, Quantity, notes, recordedBy);
    }

    // Whole or in parts to the thousandth (a kilo and a half: 1.5), never below zero.
    private static void CheckQuantity(decimal quantity, bool allowZero)
    {
        if (quantity < 0 || (!allowZero && quantity == 0) || quantity > MaxQuantity)
            throw new DomainException($"Enter a quantity{(allowZero ? " of zero or more" : " over zero")}, up to {MaxQuantity:N0}.");
        if (decimal.Round(quantity, 3) != quantity)
            throw new DomainException("A quantity goes to three decimal places at most.");
    }

    private static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

// A consumable's movement (حركة): stock received, used up, or counted, on a day. Quantity: what
// came in or went out; a count's, the difference it found (negative when short). UnitCost: what a
// unit cost when received, else the average then, so a movement's cost does not change after.
// StockAfter: what was left in the store.
public sealed class ConsumableMovement : AggregateRoot
{
    private ConsumableMovement() { }

    internal ConsumableMovement(
        Guid id,
        Guid consumableItemId,
        ConsumableMovementType type,
        DateOnly date,
        decimal quantity,
        decimal unitCost,
        decimal stockAfter,
        string? notes,
        string recordedBy)
    {
        var trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmed?.Length > ConsumableItem.MaxNotesLength)
            throw new DomainException($"Keep the notes under {ConsumableItem.MaxNotesLength} characters.");
        Id = id;
        ConsumableItemId = consumableItemId;
        Type = type;
        Date = date;
        Quantity = quantity;
        UnitCost = unitCost;
        StockAfter = stockAfter;
        Notes = trimmed;
        RecordedBy = recordedBy;
        RecordedAtUtc = DateTime.UtcNow;
    }

    public Guid ConsumableItemId { get; private set; }
    public ConsumableMovementType Type { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal StockAfter { get; private set; }
    public string? Notes { get; private set; }
    public string RecordedBy { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }

    // What it came to: a count short or over, by how much.
    public decimal Cost => Math.Round(Math.Abs(Quantity) * UnitCost, 2, MidpointRounding.AwayFromZero);
}
