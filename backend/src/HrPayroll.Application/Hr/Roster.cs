using MediatR;
using HrPayroll.Domain.Common;
using HrPayroll.Domain;
using HrPayroll.Domain.Attendance;
using HrPayroll.Domain.Employees;
using HrPayroll.Domain.Payroll;
using HrPayroll.Application.Common;

namespace HrPayroll.Application.Hr;

// Shift rostering (جداول الشفتات): the shifts (morning, evening, night), and the roster that puts
// each employee on one, or on a day off, day by day. A rostered day is measured against its shift
// instead of the employee's own work schedule (WorkSchedule, Timesheet): late, early, away and
// overtime from its hours, absent when its day passes with no punch; a rostered day off is a day
// off. A day the roster says nothing of is as the employee's own schedule says.
//
// The shifts and the roster are a manager's, or an employee's given the Attendance tab; the API
// keeps them so. A day whose month's pay is finalized is not changed.

public sealed record ShiftDto(Guid Id, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int Minutes, ShiftTone Tone, int RosteredDays)
{
    public static ShiftDto From(Shift s, int rosteredDays) => new(s.Id, s.Name, s.StartsAt, s.EndsAt, (int)s.Length.TotalMinutes, s.Tone, rosteredDays);
}

// A day of an employee's on the roster: what it is planned as (Rostered: by the roster, with its
// ShiftId, or a day off; else their own schedule: their shift's start, or a weekly day off), what
// was marked of it (leave), the public holiday it is, and whether it can be changed (employed, and
// its month's pay not finalized).
public sealed record RosterCellDto(
    DateOnly Date,
    bool Rostered,
    Guid? ShiftId,
    bool Off,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    DayMarkKind? Mark,
    string? HolidayName,
    bool Employed,
    bool Locked);

public sealed record RosterRowDto(EmployeeDto Employee, IReadOnlyList<RosterCellDto> Days);

public sealed record RosterDto(DateOnly From, DateOnly To, IReadOnlyList<ShiftDto> Shifts, IReadOnlyList<RosterRowDto> Rows);

// A change to a day on the roster: a shift (ShiftId), a day off (DayOff), or neither: back to the
// employee's own schedule.
public sealed record RosterChange(Guid EmployeeId, DateOnly Date, Guid? ShiftId, bool DayOff);

public sealed record GetShiftsQuery : IRequest<IReadOnlyList<ShiftDto>>;

internal sealed class GetShiftsHandler(IShiftRepository shifts, IRosterRepository roster) : IRequestHandler<GetShiftsQuery, IReadOnlyList<ShiftDto>>
{
    public async Task<IReadOnlyList<ShiftDto>> Handle(GetShiftsQuery request, CancellationToken cancellationToken)
    {
        var list = new List<ShiftDto>();
        foreach (var shift in await shifts.GetAllAsync(cancellationToken))
            list.Add(ShiftDto.From(shift, await roster.CountForShiftAsync(shift.Id, cancellationToken)));
        return list;
    }
}

// Added under this id, or its name, hours or colour changed: changed hours count on every day it
// is rostered whose month's pay is not finalized. Two are not named alike.
public sealed record SaveShiftCommand(Guid ShiftId, string Name, TimeOnly StartsAt, TimeOnly EndsAt, ShiftTone Tone) : IRequest<ShiftDto>, IRetryOnConflict;

internal sealed class SaveShiftHandler(IShiftRepository shifts, IRosterRepository roster, IUnitOfWork unitOfWork) : IRequestHandler<SaveShiftCommand, ShiftDto>
{
    public async Task<ShiftDto> Handle(SaveShiftCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if ((await shifts.GetAllAsync(cancellationToken))
            .FirstOrDefault(s => s.Id != request.ShiftId && string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase)) is { } same)
            throw new DomainException($"There is a {same.Name} shift already.");

        var shift = await shifts.GetByIdAsync(request.ShiftId, cancellationToken);
        if (shift is null)
            shifts.Add(shift = new Shift(request.ShiftId, name, request.StartsAt, request.EndsAt, request.Tone));
        else
        {
            shift.Rename(name);
            shift.SetTimes(request.StartsAt, request.EndsAt);
            shift.SetTone(request.Tone);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ShiftDto.From(shift, await roster.CountForShiftAsync(shift.Id, cancellationToken));
    }
}

// Deleted only while no day is rostered on it.
public sealed record DeleteShiftCommand(Guid ShiftId) : IRequest, IRetryOnConflict;

internal sealed class DeleteShiftHandler(IShiftRepository shifts, IRosterRepository roster, IUnitOfWork unitOfWork) : IRequestHandler<DeleteShiftCommand>
{
    public async Task Handle(DeleteShiftCommand request, CancellationToken cancellationToken)
    {
        if (await shifts.GetByIdAsync(request.ShiftId, cancellationToken) is not { } shift)
            return;
        var days = await roster.CountForShiftAsync(shift.Id, cancellationToken);
        if (days > 0)
            throw new DomainException($"{shift.Name} is on the roster on {days} {(days == 1 ? "day" : "days")}: put those on another shift first.");
        shifts.Remove(shift);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// The roster of a run of days, a week most often (up to MaxDays): everyone employed in it, of one
// department or all, by name, each day as planned.
public sealed record GetRosterQuery(DateOnly From, DateOnly To, Guid? DepartmentId) : IRequest<RosterDto>;

internal sealed class GetRosterHandler(
    IEmployeeRepository employees,
    IShiftRepository shifts,
    IRosterRepository roster,
    IDayMarkRepository marks,
    IPublicHolidayRepository holidays,
    IPayrollSlipRepository slips,
    Schedules schedules) : IRequestHandler<GetRosterQuery, RosterDto>
{
    public const int MaxDays = 31;

    public async Task<RosterDto> Handle(GetRosterQuery request, CancellationToken cancellationToken)
    {
        if (request.To < request.From)
            throw new DomainException("The first day must come before the last.");
        if (request.To.DayNumber - request.From.DayNumber >= MaxDays)
            throw new DomainException($"Show {MaxDays} days at most.");
        var people = (await employees.GetAllAsync(cancellationToken))
            .Where(e => e.HiredOn <= request.To && (e.LeftOn is null || e.LeftOn >= request.From))
            .Where(e => request.DepartmentId is null || e.DepartmentId == request.DepartmentId)
            .OrderBy(e => e.Name, StringComparer.CurrentCulture)
            .ToList();
        var plans = await schedules.ReadAsync(people, request.From, request.To, cancellationToken);
        var dayMarks = (await marks.GetBetweenAsync(request.From, request.To, cancellationToken)).ToDictionary(m => (m.EmployeeId, m.Date));
        var publicHolidays = await holidays.GetBetweenAsync(request.From, request.To, cancellationToken);
        var finalized = new HashSet<(Guid, DateOnly)>();
        for (var month = PayrollMonth.Of(request.From); month <= request.To; month = month.AddMonths(1))
        {
            foreach (var slip in (await slips.GetForMonthAsync(month, cancellationToken)).Where(s => s.IsFinalized))
                finalized.Add((slip.EmployeeId, month));
        }

        var rows = people.Select(e =>
        {
            var schedule = plans[e.Id];
            var days = new List<RosterCellDto>();
            for (var day = request.From; day <= request.To; day = day.AddDays(1))
            {
                var plan = schedule.Plan(day);
                var shift = plan.Off ? null : schedule.ShiftOn(day);
                days.Add(new RosterCellDto(
                    day, plan.Rostered, plan.ShiftId, plan.Off, shift is { } s ? TimeOnly.FromDateTime(s.Start) : null,
                    shift is { } t ? TimeOnly.FromDateTime(t.End) : null, dayMarks.GetValueOrDefault((e.Id, day))?.Kind,
                    publicHolidays.FirstOrDefault(h => h.Covers(day))?.Name, e.IsEmployedOn(day), finalized.Contains((e.Id, PayrollMonth.Of(day)))));
            }

            return new RosterRowDto(EmployeeDto.From(e), days);
        }).ToList();

        var list = new List<ShiftDto>();
        foreach (var shift in await shifts.GetAllAsync(cancellationToken))
            list.Add(ShiftDto.From(shift, await roster.CountForShiftAsync(shift.Id, cancellationToken)));
        return new RosterDto(request.From, request.To, list, rows);
    }
}

// Days put on a shift, given off, or taken back to the employee's own schedule, many at once (a
// week's, a whole row's). Each while employed and its month's pay open; and no shift starts before
// the one of the day before it ends.
public sealed record SetRosterCommand(IReadOnlyList<RosterChange> Changes, string StaffName) : IRequest<int>, IRetryOnConflict;

internal sealed class SetRosterHandler(
    IEmployeeRepository employees,
    IShiftRepository shifts,
    IRosterRepository roster,
    IPayrollSlipRepository slips,
    IUnitOfWork unitOfWork,
    AttendanceBroadcast broadcast) : IRequestHandler<SetRosterCommand, int>
{
    public const int MaxChanges = 500;

    public async Task<int> Handle(SetRosterCommand request, CancellationToken cancellationToken)
    {
        var changes = request.Changes
            .GroupBy(c => (c.EmployeeId, c.Date))
            .Select(g => g.Last())
            .ToList();
        if (changes.Count is 0 or > MaxChanges)
            throw new DomainException($"Change one day to {MaxChanges} at a time.");
        var byId = (await shifts.GetAllAsync(cancellationToken)).ToDictionary(s => s.Id);
        foreach (var change in changes.Where(c => !c.DayOff && c.ShiftId is { } id && !byId.ContainsKey(id)))
            throw new DomainException("That shift is not there any more: reload the roster.");

        foreach (var group in changes.GroupBy(c => c.EmployeeId))
        {
            var employee = await employees.GetRequiredAsync(group.Key, cancellationToken);
            var from = group.Min(c => c.Date);
            var to = group.Max(c => c.Date);
            foreach (var change in group)
            {
                if (!employee.IsEmployedOn(change.Date))
                    throw new DomainException($"{employee.Name} is not employed on {change.Date:d MMM yyyy}.");
                await slips.EnsureOpenAsync(employee, change.Date, cancellationToken);
            }

            // The days as they will be, and those either side of them, to check one against the next.
            var days = (await roster.GetBetweenAsync(from.AddDays(-1), to.AddDays(1), employee.Id, cancellationToken)).ToDictionary(d => d.Date);
            foreach (var change in group)
            {
                var shiftId = change.DayOff ? null : change.ShiftId;
                var existing = await roster.GetAsync(employee.Id, change.Date, cancellationToken);
                if (!change.DayOff && change.ShiftId is null)
                {
                    if (existing is not null)
                        roster.Remove(existing);
                    days.Remove(change.Date);
                    continue;
                }

                if (existing is null)
                    roster.Add(existing = new RosterDay(employee.Id, change.Date, shiftId, request.StaffName));
                else
                    existing.Set(shiftId, request.StaffName);
                days[change.Date] = existing;
            }

            var schedule = new WorkSchedule(employee, days.Values, byId);
            for (var day = from; day <= to.AddDays(1); day = day.AddDays(1))
            {
                if (schedule.ShiftOn(day.AddDays(-1)) is { } before && schedule.ShiftOn(day) is { } shift && shift.Start < before.End)
                {
                    var plan = schedule.Plan(day);
                    throw new DomainException(
                        $"{employee.Name}'s {plan.ShiftName ?? "shift"} on {day:ddd d MMM} would start at {shift.Start:HH:mm}, before the day before's ends at {before.End:HH:mm}.");
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await broadcast.ChangedAsync(changes.Select(c => c.EmployeeId).Distinct().ToList(), cancellationToken);
        return changes.Count;
    }
}
