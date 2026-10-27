using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Payroll;

// What an employee was paid of a month's net pay (صرف الراتب), on a day, as the business paid it
// (cash, a transfer): an HR record, so the payslip says what is paid and what is still owed.
// Paid against a finalized slip, never more than it still owes; voided while it stands, a
// mistake taken back.
public sealed class SalaryPayment : AggregateRoot
{
    public const int MaxNotesLength = 300;

    private SalaryPayment() { }

    public SalaryPayment(Guid id, Guid employeeId, DateOnly payrollMonth, DateOnly date, decimal amount, string? notes, string recordedBy)
    {
        if (amount <= 0)
            throw new DomainException("Enter an amount more than nothing.");
        if (notes?.Trim().Length > MaxNotesLength)
            throw new DomainException($"Keep the notes under {MaxNotesLength} characters.");
        if (date < Payroll.PayrollMonth.Required(payrollMonth))
            throw new DomainException("A month's pay is paid on a day of that month or after it.");
        Id = id;
        EmployeeId = employeeId;
        PayrollMonth = payrollMonth;
        Date = date;
        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        RecordedBy = recordedBy;
        RecordedAtUtc = DateTime.UtcNow;
    }

    public Guid EmployeeId { get; private set; }
    public DateOnly PayrollMonth { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Amount { get; private set; }
    public string? Notes { get; private set; }
    public string RecordedBy { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }

    public DateTime? VoidedAtUtc { get; private set; }
    public string? VoidedBy { get; private set; }
    public bool IsVoided => VoidedAtUtc is not null;

    public void Void(string voidedBy)
    {
        if (IsVoided)
            throw new DomainException("This payment has already been voided.");
        VoidedAtUtc = DateTime.UtcNow;
        VoidedBy = voidedBy;
    }
}
