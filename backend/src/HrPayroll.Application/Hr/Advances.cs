using System.Globalization;
using MediatR;
using HrPayroll.Domain.Common;
using HrPayroll.Domain;
using HrPayroll.Domain.Payroll;
using HrPayroll.Application.Approvals;
using HrPayroll.Application.Common;
using HrPayroll.Domain.Approvals;

namespace HrPayroll.Application.Hr;

// HR's advances (السلفيات): what is deducted from whose pay, and what was handed out with no one
// named yet.
//
// An advance is an HR record (HrAdvance): money handed out ahead of pay, as the business paid it.
// Paid to an employee it is deducted from their pay; handed out as a lump sum with no one named
// (السلفيات المعلقة) it waits here, and a manager gives it out, a share each (AdvanceAllocation),
// in parts if need be, each share deducted from its employee's pay as their own advance would be.
// Fully allocated once the shares add up to it. A share is taken back while the pay it is
// deducted from is not finalized; a lump sum is voided only once no share of it stands.

public enum AdvanceOrigin
{
    // Paid to the employee.
    Direct = 1,

    // Their share of advances handed out with no one named.
    Share = 2,
}

// An advance deducted from an employee's pay of PayrollMonth. Id is the advance's (Direct) or the
// share's (Share); AdvanceId, the advance a share is of.
public sealed record AllocatedAdvanceDto(
    Guid Id, AdvanceOrigin Origin, Guid? AdvanceId, Guid EmployeeId, string Employee, decimal Amount, DateOnly Date, DateOnly PayrollMonth,
    string? Notes, string RecordedBy);

public sealed record AdvanceShareDto(
    Guid Id, Guid EmployeeId, string Employee, decimal Amount, DateOnly PayrollMonth, string? Notes, string AllocatedBy, DateTime AllocatedAtUtc);

// Advances handed out with no one named: how much, how much is given out, what is left, and the
// shares standing.
public sealed record PendingAdvanceDto(
    Guid Id, DateOnly Date, decimal Amount, decimal Allocated, decimal Remaining, string? Notes, string RecordedBy, IReadOnlyList<AdvanceShareDto> Shares)
{
    public bool FullyAllocated => Remaining == 0;
}

public sealed record AdvancesDto(DateOnly Month, IReadOnlyList<AllocatedAdvanceDto> Allocated, IReadOnlyList<PendingAdvanceDto> Pending);

internal static class Advances
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static PendingAdvanceDto Pending(HrAdvance lump, IEnumerable<AdvanceAllocation> shares, Func<Guid, string> name)
    {
        var standing = shares.Where(a => a.AdvanceId == lump.Id && !a.IsVoided).OrderBy(a => a.AllocatedAtUtc).ToList();
        var allocated = standing.Sum(a => a.Amount);
        return new PendingAdvanceDto(
            lump.Id, lump.Date, lump.Amount, allocated, lump.Amount - allocated, lump.Notes, lump.RecordedBy,
            standing.Select(a => new AdvanceShareDto(a.Id, a.EmployeeId, name(a.EmployeeId), a.Amount, a.PayrollMonth, a.Notes, a.AllocatedBy, a.AllocatedAtUtc))
                .ToList());
    }

    // The lump sum of this id; refused when it names an employee.
    public static async Task<HrAdvance> LumpAsync(IHrAdvanceRepository hr, Guid id, CancellationToken cancellationToken)
    {
        var advance = await hr.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("Advance", id);
        return advance.IsPending
            ? advance
            : throw new DomainException("Only advances handed out with no one named are given out here: this one is an employee's already.");
    }

    // The month an advance handed out on `date` counts in for an employee: its own, or the first
    // after it whose pay is not finalized for them.
    public static async Task<DateOnly> OpenMonthAsync(IPayrollSlipRepository slips, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var month = PayrollMonth.Of(date);
        while (await slips.GetAsync(employeeId, month, cancellationToken) is { IsFinalized: true })
            month = month.AddMonths(1);
        return month;
    }

    public static string Day(DateOnly day) => day.ToString("d MMM yyyy", Invariant);

    public static string MonthName(DateOnly month) => month.ToString("MMMM yyyy", Invariant);

    public static async Task<Func<Guid, string>> NamesAsync(IEmployeeRepository employees, CancellationToken cancellationToken)
    {
        var names = (await employees.GetAllAsync(cancellationToken)).ToDictionary(e => e.Id, e => e.Name);
        return id => names.GetValueOrDefault(id) ?? "A removed employee";
    }
}

// A payroll month's advances, each deducted from whose pay; and the advances handed out with no
// one named: every one not all given out, with those all given out in the month.
public sealed record GetAdvancesQuery(DateOnly Month) : IRequest<AdvancesDto>;

internal sealed class GetAdvancesHandler(IHrAdvanceRepository hr, IAdvanceAllocationRepository allocations, IEmployeeRepository employees)
    : IRequestHandler<GetAdvancesQuery, AdvancesDto>
{
    public async Task<AdvancesDto> Handle(GetAdvancesQuery request, CancellationToken cancellationToken)
    {
        var month = PayrollMonth.Required(request.Month);
        var name = await Advances.NamesAsync(employees, cancellationToken);
        var lumps = (await hr.GetPendingAsync(cancellationToken)).OrderByDescending(l => l.Date).ToList();

        var direct = (await hr.GetForPayrollMonthAsync(month, null, cancellationToken))
            .Where(a => !a.IsVoided)
            .Select(a => new AllocatedAdvanceDto(
                a.Id, AdvanceOrigin.Direct, a.Id, a.EmployeeId!.Value, name(a.EmployeeId.Value), a.Amount, a.Date, month, a.Notes, a.RecordedBy));
        var shared = (await allocations.GetForPayrollMonthAsync(month, null, cancellationToken))
            .Where(a => !a.IsVoided)
            .Select(a => new AllocatedAdvanceDto(
                a.Id, AdvanceOrigin.Share, a.AdvanceId, a.EmployeeId, name(a.EmployeeId), a.Amount, a.Date, month, a.Notes, a.AllocatedBy));

        var shares = await allocations.GetForAdvancesAsync(lumps.Select(l => l.Id).ToList(), cancellationToken);
        var waiting = lumps.Select(l => Advances.Pending(l, shares, name))
            .Where(p => p.Remaining > 0 || PayrollMonth.Of(p.Date) == month)
            .ToList();
        return new AdvancesDto(
            month, direct.Concat(shared).OrderByDescending(a => a.Date).ThenBy(a => a.Employee, StringComparer.CurrentCulture).ToList(), waiting);
    }
}

public sealed record AdvanceShareRequest(Guid EmployeeId, decimal Amount, string? Notes = null);

// Advances handed out with no one named, given out (in part, or the rest of them): a share each,
// deducted from each employee's pay. A manager's.
public sealed record DistributeAdvanceCommand(Guid AdvanceId, IReadOnlyList<AdvanceShareRequest> Shares, string StaffName)
    : IRequest<PendingAdvanceDto>, IRetryOnConflict;

internal sealed class DistributeAdvanceHandler(
    IHrAdvanceRepository hr,
    IAdvanceAllocationRepository allocations,
    IEmployeeRepository employees,
    IPayrollSlipRepository slips,
    ICompanyProfile company,
    IUnitOfWork unitOfWork) : IRequestHandler<DistributeAdvanceCommand, PendingAdvanceDto>
{
    public async Task<PendingAdvanceDto> Handle(DistributeAdvanceCommand request, CancellationToken cancellationToken)
    {
        var lump = await Advances.LumpAsync(hr, request.AdvanceId, cancellationToken);
        if (lump.IsVoided)
            throw new DomainException("These advances were voided: there is nothing to give out.");
        if (request.Shares.Count == 0)
            throw new DomainException("Give at least one employee a share.");

        var name = await Advances.NamesAsync(employees, cancellationToken);
        if (request.Shares.GroupBy(s => s.EmployeeId).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new DomainException($"{name(twice.Key)} is on the list twice: give them one share.");
        var standing = (await allocations.GetForAdvancesAsync([lump.Id], cancellationToken)).Where(a => !a.IsVoided).ToList();
        var remaining = lump.Amount - standing.Sum(a => a.Amount);
        var total = request.Shares.Sum(s => Math.Round(s.Amount, 2, MidpointRounding.AwayFromZero));
        if (remaining <= 0)
            throw new DomainException("These advances are all given out already.");
        if (total > remaining)
        {
            var money = company.Current.Money;
            throw new DomainException(
                $"{money.Format(remaining)} of these advances is left to give out, and the shares come to {money.Format(total)}: give out that much or less.");
        }

        foreach (var share in request.Shares)
        {
            var employee = await employees.GetByIdAsync(share.EmployeeId, cancellationToken) ?? throw new NotFoundException("Employee", share.EmployeeId);
            if (!employee.IsEmployedOn(lump.Date))
                throw new DomainException($"{employee.Name} was not employed on {Advances.Day(lump.Date)}, when these advances were handed out.");
            var month = await Advances.OpenMonthAsync(slips, employee.Id, lump.Date, cancellationToken);
            var made = new AdvanceAllocation(Guid.NewGuid(), lump.Id, employee.Id, share.Amount, lump.Date, month, share.Notes, request.StaffName);
            allocations.Add(made);
            standing.Add(made);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Advances.Pending(lump, standing, name);
    }
}

// A share taken back, for the advances to be given out again: while the pay it is deducted from
// is not finalized. A manager's.
public sealed record TakeBackAdvanceShareCommand(Guid ShareId, string StaffName) : IRequest<PendingAdvanceDto>;

internal sealed class TakeBackAdvanceShareHandler(
    IHrAdvanceRepository hr,
    IAdvanceAllocationRepository allocations,
    IEmployeeRepository employees,
    IPayrollSlipRepository slips,
    IUnitOfWork unitOfWork) : IRequestHandler<TakeBackAdvanceShareCommand, PendingAdvanceDto>
{
    public async Task<PendingAdvanceDto> Handle(TakeBackAdvanceShareCommand request, CancellationToken cancellationToken)
    {
        var share = await allocations.GetByIdAsync(request.ShareId, cancellationToken) ?? throw new NotFoundException("Advance share", request.ShareId);
        var name = await Advances.NamesAsync(employees, cancellationToken);
        if (await slips.GetAsync(share.EmployeeId, share.PayrollMonth, cancellationToken) is { IsFinalized: true })
            throw new DomainException(
                $"{name(share.EmployeeId)}'s pay for {Advances.MonthName(share.PayrollMonth)} is finalized with this share deducted: it cannot be taken back now.");
        share.Void(request.StaffName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var lump = await Advances.LumpAsync(hr, share.AdvanceId, cancellationToken);
        return Advances.Pending(lump, await allocations.GetForAdvancesAsync([lump.Id], cancellationToken), name);
    }
}

// ---- Recording an advance ----

// What is asked: how much, the day, to whom (no one yet: a lump sum, given out a share each), and
// a note.
public sealed record HrAdvanceInput(decimal Amount, DateOnly Date, Guid? EmployeeId, string? Notes);

public sealed record HrAdvanceDto(
    Guid Id, decimal Amount, DateOnly Date, Guid? EmployeeId, string? Employee, DateOnly? PayrollMonth, string? Notes, string RecordedBy, string? ApprovedBy,
    DateTime? VoidedAtUtc)
{
    public static HrAdvanceDto From(HrAdvance a, Func<Guid, string> name) => new(
        a.Id, a.Amount, a.Date, a.EmployeeId, a.EmployeeId is { } id ? name(id) : null, a.PayrollMonth, a.Notes, a.RecordedBy, a.ApprovedBy, a.VoidedAtUtc);
}

public sealed record HrAdvanceResultDto(ApprovalOutcome Outcome, HrAdvanceDto? Advance, ApprovalRequestDto? Request);

// Checks, describes and makes an advance: for a manager's own and for an approval alike. What it makes is added for the caller to save.
internal sealed class HrAdvanceWriter(IHrAdvanceRepository advances, IEmployeeRepository employees, IPayrollSlipRepository slips, ICompanyProfile company)
{
    public async Task CheckAsync(HrAdvanceInput input, CancellationToken cancellationToken)
    {
        if (input.Amount <= 0)
            throw new DomainException("Enter an amount more than nothing.");
        if (input.Notes?.Trim().Length > HrAdvance.MaxNotesLength)
            throw new DomainException($"Keep the notes under {HrAdvance.MaxNotesLength} characters.");
        if (input.EmployeeId is { } id)
        {
            var employee = await employees.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("Employee", id);
            if (!employee.IsEmployedOn(input.Date))
                throw new DomainException($"{employee.Name} is not employed on {Advances.Day(input.Date)}.");
        }
    }

    // "Advance · Hassan · L.E 500.00", in the company's money, and what it is, line by line.
    public async Task<(string Summary, IReadOnlyList<ApprovalChange> Changes)> DescribeAsync(HrAdvanceInput input, CancellationToken cancellationToken)
    {
        var amount = company.Current.Money.Format(Math.Round(input.Amount, 2, MidpointRounding.AwayFromZero));
        if (input.EmployeeId is not { } id)
            return ($"Advances not given out yet · {amount}",
                [new ApprovalChange("Employee", null, "None yet: a manager gives it out on HR → Advances")]);
        var employee = await employees.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("Employee", id);
        return ($"Advance · {employee.Name} · {amount}",
            [
                new ApprovalChange("Employee", null, employee.Name),
                new ApprovalChange("Deducted from the pay of", null, Advances.MonthName(await Advances.OpenMonthAsync(slips, id, input.Date, cancellationToken))),
            ]);
    }

    public async Task<HrAdvance> ApplyAsync(Guid id, HrAdvanceInput input, string recordedBy, string? approvedBy, CancellationToken cancellationToken)
    {
        await CheckAsync(input, cancellationToken);
        DateOnly? month = input.EmployeeId is { } employee ? await Advances.OpenMonthAsync(slips, employee, input.Date, cancellationToken) : null;
        var advance = new HrAdvance(id, input.Amount, input.Date, input.EmployeeId, month, input.Notes, recordedBy, approvedBy);
        advances.Add(advance);
        return advance;
    }
}

// An advance recorded: a manager's at once; anyone else's asked of a manager (module Advance),
// recorded only when approved. AdvanceId and RequestId are made by the app: a retry finds what
// was made, or asked.
public sealed record RecordHrAdvanceCommand(
    Guid AdvanceId, Guid RequestId, HrAdvanceInput Advance, string StaffId, string StaffName, bool CanApprove) : IRequest<HrAdvanceResultDto>, IRetryOnConflict;

internal sealed class RecordHrAdvanceHandler(
    HrAdvanceWriter writer,
    IHrAdvanceRepository advances,
    IApprovalRequestRepository requests,
    IEmployeeRepository employees,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordHrAdvanceCommand, HrAdvanceResultDto>
{
    public async Task<HrAdvanceResultDto> Handle(RecordHrAdvanceCommand request, CancellationToken cancellationToken)
    {
        var name = await Advances.NamesAsync(employees, cancellationToken);
        if (await advances.GetByIdAsync(request.AdvanceId, cancellationToken) is { } made)
            return new HrAdvanceResultDto(ApprovalOutcome.Applied, HrAdvanceDto.From(made, name), null);
        if (await requests.GetByIdAsync(request.RequestId, cancellationToken) is { } asked)
            return new HrAdvanceResultDto(ApprovalOutcome.Requested, null, ApprovalRequestDto.From(asked));

        if (request.CanApprove)
        {
            var advance = await writer.ApplyAsync(request.AdvanceId, request.Advance, request.StaffName, null, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new HrAdvanceResultDto(ApprovalOutcome.Applied, HrAdvanceDto.From(advance, name), null);
        }

        await writer.CheckAsync(request.Advance, cancellationToken);
        var (summary, changes) = await writer.DescribeAsync(request.Advance, cancellationToken);
        var asking = ApprovalRequest.AskToChange(
            request.RequestId, ApprovalModule.Advance, ApprovalAction.Create, request.AdvanceId, summary, changes, ApprovalPayload.Write(request.Advance),
            null, request.StaffId, request.StaffName);
        requests.Add(asking);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new HrAdvanceResultDto(ApprovalOutcome.Requested, null, ApprovalRequestDto.From(asking));
    }
}

// An advance voided: while the pay it is deducted from is not finalized,
// and, a lump sum, once no share of it stands. A manager's.
public sealed record VoidHrAdvanceCommand(Guid AdvanceId, string StaffName) : IRequest<HrAdvanceDto>;

internal sealed class VoidHrAdvanceHandler(
    IHrAdvanceRepository advances,
    IAdvanceAllocationRepository allocations,
    IEmployeeRepository employees,
    IPayrollSlipRepository slips,
    IUnitOfWork unitOfWork) : IRequestHandler<VoidHrAdvanceCommand, HrAdvanceDto>
{
    public async Task<HrAdvanceDto> Handle(VoidHrAdvanceCommand request, CancellationToken cancellationToken)
    {
        var advance = await advances.GetByIdAsync(request.AdvanceId, cancellationToken) ?? throw new NotFoundException("Advance", request.AdvanceId);
        var name = await Advances.NamesAsync(employees, cancellationToken);
        if (advance is { EmployeeId: { } employee, PayrollMonth: { } month }
            && await slips.GetAsync(employee, month, cancellationToken) is { IsFinalized: true })
            throw new DomainException($"This advance is deducted from the finalized pay of {Advances.MonthName(month)}: it cannot be voided now.");
        if (advance.IsPending && (await allocations.GetForAdvancesAsync([advance.Id], cancellationToken)).Any(a => !a.IsVoided))
            throw new DomainException("Part of these advances is given out to employees: take those shares back first.");
        advance.Void(request.StaffName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return HrAdvanceDto.From(advance, name);
    }
}
