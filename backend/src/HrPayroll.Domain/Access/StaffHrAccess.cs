using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Access;

// The HR & Payroll tabs a system user who is not a manager sees: an accountant's, by their user
// id. None of these for a user: every tab; once a manager ticks theirs, only those.
public sealed class StaffHrAccess : AggregateRoot
{
    public const int MaxStaffIdLength = 450;

    private StaffHrAccess() { }

    public StaffHrAccess(string staffId, HrTabs tabs, string setBy)
    {
        if (string.IsNullOrWhiteSpace(staffId))
            throw new DomainException("Say whose tabs these are.");
        StaffId = staffId.Trim();
        Set(tabs, setBy);
    }

    public string StaffId { get; private set; } = string.Empty;
    public HrTabs Tabs { get; private set; }
    public string SetBy { get; private set; } = string.Empty;
    public DateTime SetAtUtc { get; private set; }

    public void Set(HrTabs tabs, string setBy)
    {
        Tabs = tabs & HrTabs.All;
        SetBy = setBy;
        SetAtUtc = DateTime.UtcNow;
    }
}
