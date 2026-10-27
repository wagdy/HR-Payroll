using MediatR;
using HrPayroll.Domain.Common;
using HrPayroll.Domain;
using HrPayroll.Domain.Attendance;
using HrPayroll.Domain.Employees;
using HrPayroll.Domain.Payroll;
using HrPayroll.Application.Common;

namespace HrPayroll.Application.Hr;

// The monthly payroll (مسير الرواتب): each employee's slip worked out from their salary, their
// days, the month's incentives and leave allowances, the advances they had against it, and what
// last month's slip carried over. A draft is worked out afresh whenever it is read; calculating
// saves the drafts; finalizing one fixes it, with the days it was worked out from. What an
// employee is paid of a finalized slip is recorded as a salary payment (SalaryPayment).
//
// Incentives, allowances and finalizing are a manager's; the API keeps them to managers.

public sealed record PayrollAdjustmentDto(
    Guid Id, Guid EmployeeId, DateOnly Month, AdjustmentKind Kind, decimal Amount, string? Reason, AdjustmentSource Source, DateOnly? ForDay,
    string RecordedBy, DateTime RecordedAtUtc, DateTime? VoidedAtUtc, string? VoidedBy)
{
    public static PayrollAdjustmentDto From(PayrollAdjustment a) => new(
        a.Id, a.EmployeeId, a.Month, a.Kind, a.Amount, a.Reason, a.Source, a.ForDay, a.RecordedBy, a.RecordedAtUtc, a.VoidedAtUtc, a.VoidedBy);
}

// An advance deducted in the month: their own, or their share of advances handed out with no one
// named (IsShare).
public sealed record PayrollAdvanceDto(Guid Id, DateOnly Date, decimal Amount, string? Notes, string RecordedBy, string? ApprovedBy, bool IsShare);

// What was paid of the month's pay; a voided payment is shown, marked.
public sealed record SalaryPaymentDto(Guid Id, DateOnly Date, decimal Amount, string? Notes, string RecordedBy, DateTime? VoidedAtUtc, string? VoidedBy)
{
    public static SalaryPaymentDto From(SalaryPayment p) => new(p.Id, p.Date, p.Amount, p.Notes, p.RecordedBy, p.VoidedAtUtc, p.VoidedBy);
}

public sealed record PayrollSlipDto(
    EmployeeDto Employee,
    DateOnly Month,
    // Null for a draft not yet saved: worked out now, as it would be.
    PayrollSlipStatus? Status,
    PayrollFigures Figures,
    decimal SalaryPaid,
    decimal SalaryOwed,
    DateTime? CalculatedAtUtc,
    DateTime? FinalizedAtUtc,
    string? FinalizedBy,
    IReadOnlyList<PayrollAdjustmentDto> Adjustments,
    IReadOnlyList<PayrollAdvanceDto> Advances,
    IReadOnlyList<SalaryPaymentDto> Salaries,
    // What the penalties and unpaid leave deducted are, day by day, as the days stand now.
    IReadOnlyList<PayslipPenaltyDto> Penalties,
    IReadOnlyList<DateOnly> UnpaidLeaveDays);

// A penalty on the slip: hours at the hourly rate, or an amount as it is (a disciplinary action's
// penalty deduction, linked to it).
public sealed record PayslipPenaltyDto(DateOnly Date, decimal Hours, decimal? Amount, string Reason, Guid? DisciplinaryActionId);

// Gross: the month's salaries (إجمالي الرواتب), what is earned before anything is deducted.
public sealed record PayrollTotals(
    decimal BaseSalaries, decimal Incentives, decimal LeaveAllowances, decimal OvertimePay, decimal Advances, decimal BroughtForward,
    decimal LatenessPenalties, decimal AbsencePenalties, decimal NetPayable, decimal CarriedForward, decimal WagesCost, decimal SalaryPaid,
    decimal DaysOffWorkedPay, decimal HolidaysWorkedPay, decimal Gross = 0, decimal AdministrativePenalties = 0, decimal UnpaidLeaveDeductions = 0);

public sealed record PayrollMonthDto(DateOnly Month, PayrollTotals Totals, IReadOnlyList<PayrollSlipDto> Slips);

// Advances: paid to them; Shares: theirs of advances handed out with no one named; both deducted.
// Payments: what was paid of the month's pay, voided ones too.
public sealed record WorkedOut(
    Employee Employee, PayrollFigures Figures, PayrollSlip? Slip, IReadOnlyList<PayrollAdjustment> Adjustments, IReadOnlyList<SalaryPayment> Payments,
    EmployeeSheet Sheet, IReadOnlyList<AdvanceAllocation> Shares, IReadOnlyList<HrAdvance> Advances);

internal sealed class PayrollWorker(
    IEmployeeRepository employees,
    Timesheets timesheets,
    IPayrollAdjustmentRepository adjustments,
    IPayrollSlipRepository slips,
    ISalaryPaymentRepository payments,
    IAdvanceAllocationRepository allocations,
    IHrAdvanceRepository hrAdvances)
{
    // Everyone employed in the month, or one employee, as their pay stands now: a finalized slip's
    // figures as they were finalized, a draft's worked out afresh.
    public async Task<IReadOnlyList<WorkedOut>> WorkOutAsync(DateOnly month, Guid? employeeId, CancellationToken cancellationToken)
    {
        PayrollMonth.Required(month);
        var last = month.AddMonths(1).AddDays(-1);
        IReadOnlyList<Employee> everyone = employeeId is { } id
            ? [await employees.GetRequiredAsync(id, cancellationToken)]
            : await employees.GetAllAsync(cancellationToken);
        var people = everyone.Where(e => e.HiredOn <= last && (e.LeftOn is null || e.LeftOn >= month)).ToList();
        if (people.Count == 0)
            return [];

        var (_, sheets) = await timesheets.ReadAsync(people, month, last, cancellationToken);
        var monthAdjustments = await adjustments.GetForMonthAsync(month, cancellationToken);
        var paid = await payments.GetForPayrollMonthAsync(month, employeeId, cancellationToken);
        var shares = (await allocations.GetForPayrollMonthAsync(month, employeeId, cancellationToken)).Where(a => !a.IsVoided).ToList();
        var advances = (await hrAdvances.GetForPayrollMonthAsync(month, employeeId, cancellationToken)).Where(a => !a.IsVoided).ToList();
        var saved = (await slips.GetForMonthAsync(month, cancellationToken)).ToDictionary(s => s.EmployeeId);
        var before = (await slips.GetForMonthAsync(month.AddMonths(-1), cancellationToken)).ToDictionary(s => s.EmployeeId);

        return sheets.Select(sheet =>
        {
            var e = sheet.Employee;
            var theirs = monthAdjustments.Where(a => a.EmployeeId == e.Id).ToList();
            var theirPayments = paid.Where(p => p.EmployeeId == e.Id).ToList();
            var theirShares = shares.Where(a => a.EmployeeId == e.Id).ToList();
            var theirAdvances = advances.Where(a => a.EmployeeId == e.Id).ToList();
            var slip = saved.GetValueOrDefault(e.Id);
            var figures = slip is { IsFinalized: true }
                ? slip.Figures
                : PayrollCalculator.Calculate(
                    e, month, sheet.Days.Select(d => d.Evaluation), theirs,
                    theirAdvances.Sum(a => a.Amount) + theirShares.Sum(a => a.Amount),
                    before.GetValueOrDefault(e.Id) is { IsFinalized: true } previous ? previous.CarriedForward : 0);
            return new WorkedOut(e, figures, slip, theirs, theirPayments, sheet, theirShares, theirAdvances);
        }).ToList();
    }

    public static PayrollMonthDto Dto(DateOnly month, IReadOnlyList<WorkedOut> worked)
    {
        var slipDtos = worked.Select(SlipDto).ToList();
        decimal Sum(Func<PayrollFigures, decimal> of) => worked.Sum(w => of(w.Figures));
        return new PayrollMonthDto(
            month,
            new PayrollTotals(
                Sum(f => f.BaseSalary), Sum(f => f.Incentives), Sum(f => f.LeaveAllowances), Sum(f => f.OvertimePay), Sum(f => f.Advances),
                Sum(f => f.BroughtForward), Sum(f => f.LatenessPenalty), Sum(f => f.AbsencePenalty), Sum(f => f.NetPayable), Sum(f => f.CarriedForward),
                Sum(f => f.WagesCost), slipDtos.Sum(s => s.SalaryPaid), Sum(f => f.DaysOffWorkedPay), Sum(f => f.HolidaysWorkedPay), Sum(f => f.Gross),
                Sum(f => f.AdministrativePenalty), Sum(f => f.UnpaidLeaveDeduction)),
            slipDtos);
    }

    public static PayrollSlipDto SlipDto(WorkedOut w)
    {
        var paid = w.Payments.Where(p => !p.IsVoided).Sum(p => p.Amount);
        return new PayrollSlipDto(
            EmployeeDto.From(w.Employee), PayrollMonth.Of(w.Sheet.Days[0].Evaluation.Day), w.Slip?.Status, w.Figures, paid,
            w.Slip is { IsFinalized: true } ? Math.Max(0, w.Figures.NetPayable - paid) : 0, w.Slip?.CalculatedAtUtc, w.Slip?.FinalizedAtUtc,
            w.Slip?.FinalizedBy, w.Adjustments.OrderBy(a => a.RecordedAtUtc).Select(PayrollAdjustmentDto.From).ToList(),
            w.Advances.Select(a => new PayrollAdvanceDto(a.Id, a.Date, a.Amount, a.Notes, a.RecordedBy, a.ApprovedBy, false))
                .Concat(w.Shares.Select(a => new PayrollAdvanceDto(
                    a.Id, a.Date, a.Amount,
                    $"Share of the advances handed out on {a.Date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}" +
                    (a.Notes is { } notes ? $": {notes}" : ""), a.AllocatedBy, null, true)))
                .OrderBy(c => c.Date).ToList(),
            w.Payments.OrderBy(p => p.Date).ThenBy(p => p.RecordedAtUtc).Select(SalaryPaymentDto.From).ToList(),
            w.Sheet.Days.SelectMany(d => d.Penalties ?? [])
                .Select(p => new PayslipPenaltyDto(p.Date, p.Hours, p.Amount, p.Reason, p.DisciplinaryActionId))
                .OrderBy(p => p.Date).ToList(),
            w.Employee.IsDaily ? []
                : w.Sheet.Days.Where(d => d.Evaluation.Status == DayStatus.UnpaidLeave).Select(d => d.Evaluation.Day).ToList());
    }
}

public sealed record GetPayrollQuery(DateOnly Month) : IRequest<PayrollMonthDto>;

internal sealed class GetPayrollHandler(PayrollWorker payroll) : IRequestHandler<GetPayrollQuery, PayrollMonthDto>
{
    public async Task<PayrollMonthDto> Handle(GetPayrollQuery request, CancellationToken cancellationToken) =>
        PayrollWorker.Dto(request.Month, await payroll.WorkOutAsync(request.Month, null, cancellationToken));
}

// The month's drafts saved as they stand: every employee's slip that is not finalized.
public sealed record CalculatePayrollCommand(DateOnly Month) : IRequest<PayrollMonthDto>, IRetryOnConflict;

internal sealed class CalculatePayrollHandler(PayrollWorker payroll, IPayrollSlipRepository slips, IUnitOfWork unitOfWork)
    : IRequestHandler<CalculatePayrollCommand, PayrollMonthDto>
{
    public async Task<PayrollMonthDto> Handle(CalculatePayrollCommand request, CancellationToken cancellationToken)
    {
        var worked = await payroll.WorkOutAsync(request.Month, null, cancellationToken);
        var saved = worked.Select(w =>
        {
            if (w.Slip is { IsFinalized: true })
                return w;
            var slip = w.Slip;
            if (slip is null)
            {
                slip = new PayrollSlip(Guid.NewGuid(), w.Employee.Id, request.Month, w.Figures);
                slips.Add(slip);
            }
            else
            {
                slip.Recalculate(w.Figures);
            }

            return w with { Slip = slip };
        }).ToList();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PayrollWorker.Dto(request.Month, saved);
    }
}

// One employee's slip finalized: worked out once more, and fixed, with the days it was worked out
// from kept as they were.
public sealed record FinalizePayrollSlipCommand(DateOnly Month, Guid EmployeeId, string StaffName) : IRequest<PayrollSlipDto>, IRetryOnConflict;

internal sealed class FinalizePayrollSlipHandler(
    PayrollWorker payroll, IPayrollSlipRepository slips, IDailyRecordRepository records, IUnitOfWork unitOfWork)
    : IRequestHandler<FinalizePayrollSlipCommand, PayrollSlipDto>
{
    public async Task<PayrollSlipDto> Handle(FinalizePayrollSlipCommand request, CancellationToken cancellationToken)
    {
        var worked = (await payroll.WorkOutAsync(request.Month, request.EmployeeId, cancellationToken)).SingleOrDefault()
            ?? throw new DomainException("They were not employed that month.");
        if (worked.Slip is { IsFinalized: true })
            return PayrollWorker.SlipDto(worked);

        var slip = worked.Slip;
        if (slip is null)
        {
            slip = new PayrollSlip(Guid.NewGuid(), worked.Employee.Id, request.Month, worked.Figures);
            slips.Add(slip);
        }
        else
        {
            slip.Recalculate(worked.Figures);
        }

        slip.Finalize(request.StaffName);

        var days = worked.Sheet.Days.Select(d => d.Evaluation).Where(d => d.Status != DayStatus.NotEmployed).ToList();
        var kept = (await records.GetForEmployeeAsync(worked.Employee.Id, days.FirstOrDefault()?.Day ?? request.Month, request.Month.AddMonths(1).AddDays(-1), cancellationToken))
            .ToDictionary(r => r.Date);
        foreach (var day in days)
        {
            if (kept.GetValueOrDefault(day.Day) is { } record)
                record.Update(day);
            else
                records.Add(new DailyRecord(worked.Employee.Id, day));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PayrollWorker.SlipDto(worked with { Slip = slip });
    }
}

// An incentive or leave allowance for a month, while its slip is not finalized. AdjustmentId is
// made by the app.
public sealed record AddPayrollAdjustmentCommand(
    Guid AdjustmentId, Guid EmployeeId, DateOnly Month, AdjustmentKind Kind, decimal Amount, string? Reason, DateOnly? ForDay, string StaffName)
    : IRequest<PayrollAdjustmentDto>, IRetryOnConflict;

internal sealed class AddPayrollAdjustmentHandler(
    IEmployeeRepository employees, IPayrollAdjustmentRepository adjustments, IPayrollSlipRepository slips, IUnitOfWork unitOfWork)
    : IRequestHandler<AddPayrollAdjustmentCommand, PayrollAdjustmentDto>
{
    public async Task<PayrollAdjustmentDto> Handle(AddPayrollAdjustmentCommand request, CancellationToken cancellationToken)
    {
        if (await adjustments.GetByIdAsync(request.AdjustmentId, cancellationToken) is { } made)
            return PayrollAdjustmentDto.From(made);
        var employee = await employees.GetRequiredAsync(request.EmployeeId, cancellationToken);
        var month = PayrollMonth.Required(request.Month);
        if (employee.HiredOn > month.AddMonths(1).AddDays(-1) || employee.LeftOn < month)
            throw new DomainException($"{employee.Name} was not employed in {month:MMMM yyyy}.");
        await slips.EnsureOpenAsync(employee, month, cancellationToken);
        var adjustment = new PayrollAdjustment(
            request.AdjustmentId, employee.Id, month, request.Kind, request.Amount, request.Reason, request.StaffName, forDay: request.ForDay);
        adjustments.Add(adjustment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PayrollAdjustmentDto.From(adjustment);
    }
}

public sealed record VoidPayrollAdjustmentCommand(Guid AdjustmentId, string StaffName) : IRequest<PayrollAdjustmentDto>;

internal sealed class VoidPayrollAdjustmentHandler(
    IEmployeeRepository employees, IPayrollAdjustmentRepository adjustments, IPayrollSlipRepository slips, IUnitOfWork unitOfWork)
    : IRequestHandler<VoidPayrollAdjustmentCommand, PayrollAdjustmentDto>
{
    public async Task<PayrollAdjustmentDto> Handle(VoidPayrollAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var adjustment = await adjustments.GetRequiredAsync(request.AdjustmentId, cancellationToken);
        await slips.EnsureOpenAsync(await employees.GetRequiredAsync(adjustment.EmployeeId, cancellationToken), adjustment.Month, cancellationToken);
        adjustment.Void(request.StaffName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PayrollAdjustmentDto.From(adjustment);
    }
}

// ---- Salary payments ----

// What an employee is paid of a finalized month's net pay, on a day: never more than it still
// owes. PaymentId is made by the app: a retry finds the one made.
public sealed record RecordSalaryPaymentCommand(
    Guid PaymentId, Guid EmployeeId, DateOnly Month, DateOnly Date, decimal Amount, string? Notes, string StaffName)
    : IRequest<SalaryPaymentDto>, IRetryOnConflict;

internal sealed class RecordSalaryPaymentHandler(PayrollWorker payroll, ISalaryPaymentRepository payments, IWorkClock clock, IUnitOfWork unitOfWork)
    : IRequestHandler<RecordSalaryPaymentCommand, SalaryPaymentDto>
{
    public async Task<SalaryPaymentDto> Handle(RecordSalaryPaymentCommand request, CancellationToken cancellationToken)
    {
        if (await payments.GetByIdAsync(request.PaymentId, cancellationToken) is { } made)
            return SalaryPaymentDto.From(made);
        var month = PayrollMonth.Required(request.Month);
        var worked = (await payroll.WorkOutAsync(month, request.EmployeeId, cancellationToken)).SingleOrDefault()
            ?? throw new DomainException("They were not employed that month.");
        if (worked.Slip is not { IsFinalized: true })
            throw new DomainException($"{worked.Employee.Name}'s pay for {month:MMMM yyyy} is paid once it is finalized.");
        if (request.Date > clock.WorkdayOf(DateTime.UtcNow))
            throw new DomainException("Date the payment today or before.");
        var owed = worked.Figures.NetPayable - worked.Payments.Where(p => !p.IsVoided).Sum(p => p.Amount);
        if (Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero) > owed)
            throw new DomainException(owed > 0
                ? $"{owed:N2} of their pay is still owed: pay that much or less."
                : "Their pay for this month is paid in full.");
        var payment = new SalaryPayment(request.PaymentId, worked.Employee.Id, month, request.Date, request.Amount, request.Notes, request.StaffName);
        payments.Add(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SalaryPaymentDto.From(payment);
    }
}

// A payment entered by mistake, voided.
public sealed record VoidSalaryPaymentCommand(Guid PaymentId, string StaffName) : IRequest<SalaryPaymentDto>;

internal sealed class VoidSalaryPaymentHandler(ISalaryPaymentRepository payments, IUnitOfWork unitOfWork)
    : IRequestHandler<VoidSalaryPaymentCommand, SalaryPaymentDto>
{
    public async Task<SalaryPaymentDto> Handle(VoidSalaryPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await payments.GetRequiredAsync(request.PaymentId, cancellationToken);
        payment.Void(request.StaffName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SalaryPaymentDto.From(payment);
    }
}
