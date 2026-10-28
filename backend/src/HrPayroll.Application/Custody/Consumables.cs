using MediatR;
using HrPayroll.Domain.Common;
using HrPayroll.Domain;
using HrPayroll.Domain.Custody;
using HrPayroll.Application.Common;

namespace HrPayroll.Application.Custody;

// Consumables inventory (جرد الاستهلاكيات): the supplies kept in the store and used up, their
// stock and what it is worth, and the movements that change it: stock received, used up, counted.
// Whoever manages on the Custody tab enters them, at once: nothing here is in anyone's custody,
// so nothing waits for a manager. An item is deleted only before anything was recorded of it.

// UnitCost: the average a unit cost; TotalCost: what the stock is worth. LastMovedOn: the last
// day anything was recorded of it.
public sealed record ConsumableItemDto(
    Guid Id,
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitCost,
    decimal TotalCost,
    decimal? ReorderLevel,
    bool IsLow,
    string? Notes,
    DateOnly? LastMovedOn,
    string CreatedBy,
    DateTime CreatedAtUtc)
{
    public static ConsumableItemDto From(ConsumableItem c, DateOnly? lastMovedOn) => new(
        c.Id, c.Name, c.Unit, c.Quantity, c.UnitCost, c.TotalCost, c.ReorderLevel, c.IsLow, c.Notes, lastMovedOn, c.CreatedBy, c.CreatedAtUtc);
}

public sealed record ConsumableMovementDto(
    Guid Id,
    Guid ConsumableItemId,
    ConsumableMovementType Type,
    DateOnly Date,
    decimal Quantity,
    decimal UnitCost,
    decimal Cost,
    decimal StockAfter,
    string? Notes,
    string RecordedBy,
    DateTime RecordedAtUtc)
{
    public static ConsumableMovementDto From(ConsumableMovement m) => new(
        m.Id, m.ConsumableItemId, m.Type, m.Date, m.Quantity, m.UnitCost, m.Cost, m.StockAfter, m.Notes, m.RecordedBy, m.RecordedAtUtc);
}

public sealed record ConsumableDetailDto(ConsumableItemDto Item, IReadOnlyList<ConsumableMovementDto> Movements);

// What recording a movement answers: the item as it is now, and the movement.
public sealed record ConsumableMovementResultDto(ConsumableItemDto Item, ConsumableMovementDto Movement);

public sealed record GetConsumablesQuery : IRequest<IReadOnlyList<ConsumableItemDto>>;

internal sealed class GetConsumablesHandler(IConsumableItemRepository items, IConsumableMovementRepository movements)
    : IRequestHandler<GetConsumablesQuery, IReadOnlyList<ConsumableItemDto>>
{
    public async Task<IReadOnlyList<ConsumableItemDto>> Handle(GetConsumablesQuery request, CancellationToken cancellationToken)
    {
        var last = await movements.GetLastDatesAsync(cancellationToken);
        return (await items.GetAllAsync(cancellationToken))
            .Select(c => ConsumableItemDto.From(c, last.TryGetValue(c.Id, out var day) ? day : null))
            .ToList();
    }
}

// An item with its movements, the latest first.
public sealed record GetConsumableQuery(Guid ItemId) : IRequest<ConsumableDetailDto?>;

internal sealed class GetConsumableHandler(IConsumableItemRepository items, IConsumableMovementRepository movements)
    : IRequestHandler<GetConsumableQuery, ConsumableDetailDto?>
{
    public async Task<ConsumableDetailDto?> Handle(GetConsumableQuery request, CancellationToken cancellationToken)
    {
        if (await items.GetByIdAsync(request.ItemId, cancellationToken) is not { } item)
            return null;
        var moved = await movements.GetForItemAsync(item.Id, cancellationToken);
        return new ConsumableDetailDto(ConsumableItemDto.From(item, moved.FirstOrDefault()?.Date), moved.Select(ConsumableMovementDto.From).ToList());
    }
}

// Added under this id, or its name, unit, reorder level and notes changed; its stock changes only
// by movements. Two are not named alike.
public sealed record SaveConsumableCommand(Guid ItemId, string Name, string Unit, decimal? ReorderLevel, string? Notes, string SavedBy)
    : IRequest<ConsumableItemDto>, IRetryOnConflict;

internal sealed class SaveConsumableHandler(IConsumableItemRepository items, IConsumableMovementRepository movements, IUnitOfWork unitOfWork)
    : IRequestHandler<SaveConsumableCommand, ConsumableItemDto>
{
    public async Task<ConsumableItemDto> Handle(SaveConsumableCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if ((await items.GetAllAsync(cancellationToken))
            .FirstOrDefault(c => c.Id != request.ItemId && string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase)) is { } same)
            throw new DomainException($"There is a {same.Name} already.");

        var item = await items.GetByIdAsync(request.ItemId, cancellationToken);
        if (item is null)
            items.Add(item = new ConsumableItem(request.ItemId, name, request.Unit, request.ReorderLevel, request.SavedBy, request.Notes));
        else
        {
            item.Rename(name);
            item.SetUnit(request.Unit);
            item.SetReorderLevel(request.ReorderLevel);
            item.SetNotes(request.Notes);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ConsumableItemDto.From(item, (await movements.GetForItemAsync(item.Id, cancellationToken)).FirstOrDefault()?.Date);
    }
}

// Deleted only when nothing was ever recorded of it: its movements are its history.
public sealed record DeleteConsumableCommand(Guid ItemId) : IRequest, IRetryOnConflict;

internal sealed class DeleteConsumableHandler(IConsumableItemRepository items, IConsumableMovementRepository movements, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteConsumableCommand>
{
    public async Task Handle(DeleteConsumableCommand request, CancellationToken cancellationToken)
    {
        if (await items.GetByIdAsync(request.ItemId, cancellationToken) is not { } item)
            return;
        if (await movements.AnyForItemAsync(item.Id, cancellationToken))
            throw new DomainException($"{item.Name} has its movements recorded, so it stays.");
        items.Remove(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// Stock received (at a unit cost, or none: at the average), used up, or counted, on a day that has
// come. MovementId is made by the app: a retry finds the movement made, and makes no other.
public sealed record RecordConsumableMovementCommand(
    Guid ItemId, Guid MovementId, ConsumableMovementType Type, decimal Quantity, decimal? UnitCost, DateOnly Date, string? Notes, string RecordedBy)
    : IRequest<ConsumableMovementResultDto>, IRetryOnConflict;

internal sealed class RecordConsumableMovementHandler(
    IConsumableItemRepository items,
    IConsumableMovementRepository movements,
    IWorkClock clock,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordConsumableMovementCommand, ConsumableMovementResultDto>
{
    public async Task<ConsumableMovementResultDto> Handle(RecordConsumableMovementCommand request, CancellationToken cancellationToken)
    {
        var item = await items.GetByIdAsync(request.ItemId, cancellationToken)
            ?? throw new NotFoundException(nameof(ConsumableItem), request.ItemId);
        if (await movements.GetByIdAsync(request.MovementId, cancellationToken) is { } made)
        {
            if (made.ConsumableItemId != item.Id)
                throw new DomainException("That movement was recorded for another item.");
            return new ConsumableMovementResultDto(ConsumableItemDto.From(item, made.Date), ConsumableMovementDto.From(made));
        }
        if (request.Date > clock.WorkdayOf(DateTime.UtcNow))
            throw new DomainException("A movement is recorded on a day that has come.");

        var movement = request.Type switch
        {
            ConsumableMovementType.Received => item.Receive(request.MovementId, request.Quantity, request.UnitCost, request.Date, request.Notes, request.RecordedBy),
            ConsumableMovementType.Consumed => item.Consume(request.MovementId, request.Quantity, request.Date, request.Notes, request.RecordedBy),
            ConsumableMovementType.Counted => item.Count(request.MovementId, request.Quantity, request.Date, request.Notes, request.RecordedBy),
            _ => throw new DomainException("Say whether stock came in, was used up, or was counted."),
        };
        movements.Add(movement);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var last = (await movements.GetForItemAsync(item.Id, cancellationToken)).FirstOrDefault()?.Date ?? movement.Date;
        return new ConsumableMovementResultDto(ConsumableItemDto.From(item, last), ConsumableMovementDto.From(movement));
    }
}
