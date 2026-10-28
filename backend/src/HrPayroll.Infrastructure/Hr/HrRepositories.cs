using Microsoft.EntityFrameworkCore;
using HrPayroll.Domain;
using HrPayroll.Domain.Access;
using HrPayroll.Domain.Attendance;
using HrPayroll.Domain.Custody;
using HrPayroll.Domain.Employees;
using HrPayroll.Domain.Leaves;
using HrPayroll.Domain.Performance;
using HrPayroll.Domain.Payroll;
using HrPayroll.Application.Hr;
using HrPayroll.Infrastructure.Persistence;

namespace HrPayroll.Infrastructure.Hr;

internal sealed class EmployeeRepository(HrDbContext db) : IEmployeeRepository
{
    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Employees.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void Add(Employee aggregate) => db.Employees.Add(aggregate);

    public async Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Employees.AsNoTracking().OrderBy(e => e.Name).ToListAsync(cancellationToken);

    public Task<Employee?> GetByDeviceUserIdAsync(string deviceUserId, CancellationToken cancellationToken = default) =>
        db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.DeviceUserId == deviceUserId, cancellationToken);

    public Task<Employee?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default) =>
        db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.NationalId == nationalId, cancellationToken);

    public Task<Employee?> GetByMobileNumberAsync(string mobileNumber, CancellationToken cancellationToken = default) =>
        db.Employees.FirstOrDefaultAsync(e => e.MobileNumber == mobileNumber, cancellationToken);

    public async Task<IReadOnlyList<Employee>> GetInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default) =>
        await db.Employees.Where(e => e.DepartmentId == departmentId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Employee>> GetWithRoleAsync(Guid jobRoleId, CancellationToken cancellationToken = default) =>
        await db.Employees.Where(e => e.JobRoleId == jobRoleId).ToListAsync(cancellationToken);
}

internal sealed class StaffHrAccessRepository(HrDbContext db) : IStaffHrAccessRepository
{
    public Task<StaffHrAccess?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.StaffHrAccess.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(StaffHrAccess aggregate) => db.StaffHrAccess.Add(aggregate);

    public Task<StaffHrAccess?> GetByStaffIdAsync(string staffId, CancellationToken cancellationToken = default) =>
        db.StaffHrAccess.SingleOrDefaultAsync(a => a.StaffId == staffId, cancellationToken);

    public async Task<IReadOnlyList<StaffHrAccess>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.StaffHrAccess.AsNoTracking().ToListAsync(cancellationToken);
}

internal sealed class AssetCategoryRepository(HrDbContext db) : IAssetCategoryRepository
{
    public Task<AssetCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AssetCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(AssetCategory aggregate) => db.AssetCategories.Add(aggregate);

    public void Remove(AssetCategory category) => db.AssetCategories.Remove(category);

    public async Task<IReadOnlyList<AssetCategory>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.AssetCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
}

internal sealed class AssetItemRepository(HrDbContext db) : IAssetItemRepository
{
    public Task<AssetItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AssetItems.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(AssetItem aggregate) => db.AssetItems.Add(aggregate);

    public async Task<IReadOnlyList<AssetItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.AssetItems.AsNoTracking().OrderBy(a => a.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssetItem>> GetInCustodyOfAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await db.AssetItems.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.Status == AssetStatus.InCustody)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyInCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        db.AssetItems.AnyAsync(a => a.CategoryId == categoryId, cancellationToken);
}

internal sealed class AssetTransactionRepository(HrDbContext db) : IAssetTransactionRepository
{
    public Task<AssetTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AssetTransactions.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public void Add(AssetTransaction aggregate) => db.AssetTransactions.Add(aggregate);

    public async Task<IReadOnlyList<AssetTransaction>> GetForAssetAsync(Guid assetItemId, CancellationToken cancellationToken = default) =>
        await db.AssetTransactions.AsNoTracking()
            .Where(t => t.AssetItemId == assetItemId)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.RecordedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssetTransaction>> GetBetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await db.AssetTransactions.AsNoTracking()
            .Where(t => t.Date >= from && t.Date <= to)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.RecordedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyForAssetAsync(Guid assetItemId, CancellationToken cancellationToken = default) =>
        db.AssetTransactions.AnyAsync(t => t.AssetItemId == assetItemId, cancellationToken);
}

internal sealed class ShiftRepository(HrDbContext db) : IShiftRepository
{
    public Task<Shift?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Shifts.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public void Add(Shift aggregate) => db.Shifts.Add(aggregate);

    public void Remove(Shift shift) => db.Shifts.Remove(shift);

    public async Task<IReadOnlyList<Shift>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Shifts.AsNoTracking().OrderBy(s => s.StartsAt).ThenBy(s => s.Name).ToListAsync(cancellationToken);
}

internal sealed class RosterRepository(HrDbContext db) : IRosterRepository
{
    public Task<RosterDay?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.RosterDays.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(RosterDay aggregate) => db.RosterDays.Add(aggregate);

    public void Remove(RosterDay day) => db.RosterDays.Remove(day);

    public async Task<IReadOnlyList<RosterDay>> GetBetweenAsync(DateOnly from, DateOnly to, Guid? employeeId = null, CancellationToken cancellationToken = default) =>
        await db.RosterDays.AsNoTracking()
            .Where(r => r.Date >= from && r.Date <= to && (employeeId == null || r.EmployeeId == employeeId))
            .ToListAsync(cancellationToken);

    public Task<RosterDay?> GetAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        db.RosterDays.SingleOrDefaultAsync(r => r.EmployeeId == employeeId && r.Date == date, cancellationToken);

    public Task<int> CountForShiftAsync(Guid shiftId, CancellationToken cancellationToken = default) =>
        db.RosterDays.CountAsync(r => r.ShiftId == shiftId, cancellationToken);
}

internal sealed class ConsumableItemRepository(HrDbContext db) : IConsumableItemRepository
{
    public Task<ConsumableItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.ConsumableItems.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(ConsumableItem aggregate) => db.ConsumableItems.Add(aggregate);

    public void Remove(ConsumableItem item) => db.ConsumableItems.Remove(item);

    public async Task<IReadOnlyList<ConsumableItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.ConsumableItems.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
}

internal sealed class ConsumableMovementRepository(HrDbContext db) : IConsumableMovementRepository
{
    public Task<ConsumableMovement?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.ConsumableMovements.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public void Add(ConsumableMovement aggregate) => db.ConsumableMovements.Add(aggregate);

    public async Task<IReadOnlyList<ConsumableMovement>> GetForItemAsync(Guid consumableItemId, CancellationToken cancellationToken = default) =>
        await db.ConsumableMovements.AsNoTracking()
            .Where(m => m.ConsumableItemId == consumableItemId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.RecordedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> GetLastDatesAsync(CancellationToken cancellationToken = default) =>
        await db.ConsumableMovements.AsNoTracking()
            .GroupBy(m => m.ConsumableItemId)
            .Select(g => new { Id = g.Key, Last = g.Max(m => m.Date) })
            .ToDictionaryAsync(x => x.Id, x => x.Last, cancellationToken);

    public Task<bool> AnyForItemAsync(Guid consumableItemId, CancellationToken cancellationToken = default) =>
        db.ConsumableMovements.AnyAsync(m => m.ConsumableItemId == consumableItemId, cancellationToken);
}

// An employee's ID card photo: one row per employee, its bytes alone.
public sealed class EmployeeIdPhoto
{
    public Guid EmployeeId { get; init; }
    public byte[] Content { get; set; } = [];
}

internal sealed class EmployeeIdPhotoStore(HrDbContext db) : IEmployeeIdPhotoStore
{
    // Replaces the one there was.
    public void Put(Guid employeeId, byte[] content)
    {
        var existing = db.EmployeeIdPhotos.Local.FirstOrDefault(p => p.EmployeeId == employeeId)
            ?? db.EmployeeIdPhotos.Find(employeeId);
        if (existing is null)
            db.EmployeeIdPhotos.Add(new EmployeeIdPhoto { EmployeeId = employeeId, Content = content });
        else
            existing.Content = content;
    }

    public async Task RemoveAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (await db.EmployeeIdPhotos.FindAsync([employeeId], cancellationToken) is { } photo)
            db.EmployeeIdPhotos.Remove(photo);
    }

    public async Task<byte[]?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await db.EmployeeIdPhotos.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .Select(p => p.Content)
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class JobRoleRepository(HrDbContext db) : IJobRoleRepository
{
    public Task<JobRole?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.JobRoles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(JobRole aggregate) => db.JobRoles.Add(aggregate);

    public void Remove(JobRole role) => db.JobRoles.Remove(role);

    public async Task<IReadOnlyList<JobRole>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.JobRoles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<JobRole>> GetInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default) =>
        await db.JobRoles.Where(r => r.DepartmentId == departmentId).ToListAsync(cancellationToken);
}

internal sealed class DepartmentRepository(HrDbContext db) : IDepartmentRepository
{
    public Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Departments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public void Add(Department aggregate) => db.Departments.Add(aggregate);

    public void Remove(Department department) => db.Departments.Remove(department);

    public async Task<IReadOnlyList<Department>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken);
}

internal sealed class AttendanceDeviceRepository(HrDbContext db) : IAttendanceDeviceRepository
{
    public Task<AttendanceDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AttendanceDevices.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public void Add(AttendanceDevice aggregate) => db.AttendanceDevices.Add(aggregate);

    public Task<AttendanceDevice?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken = default) =>
        db.AttendanceDevices.SingleOrDefaultAsync(d => d.SerialNumber == serialNumber, cancellationToken);

    public async Task<IReadOnlyList<AttendanceDevice>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.AttendanceDevices.AsNoTracking().OrderBy(d => d.FirstSeenAtUtc).ToListAsync(cancellationToken);
}

internal sealed class AttendanceLogRepository(HrDbContext db) : IAttendanceLogRepository
{
    public Task<AttendanceLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AttendanceLogs.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public void Add(AttendanceLog aggregate) => db.AttendanceLogs.Add(aggregate);

    public async Task<IReadOnlyList<AttendanceLog>> GetFromDeviceAsync(
        string serialNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        await db.AttendanceLogs.AsNoTracking()
            .Where(l => l.DeviceSerialNumber == serialNumber && l.PunchedAt >= from && l.PunchedAt <= to)
            .ToListAsync(cancellationToken);

    public Task<AttendanceLog?> GetLastReceivedFromDeviceAsync(string serialNumber, CancellationToken cancellationToken = default) =>
        db.AttendanceLogs.AsNoTracking()
            .Where(l => l.DeviceSerialNumber == serialNumber && l.IgnoredAtUtc == null)
            .OrderByDescending(l => l.ReceivedAtUtc).ThenByDescending(l => l.PunchedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceLog>> GetBetweenAsync(
        DateTime from, DateTime to, Guid? employeeId = null, CancellationToken cancellationToken = default) =>
        await db.AttendanceLogs.AsNoTracking()
            .Where(l => l.PunchedAt >= from && l.PunchedAt < to && (employeeId == null || l.EmployeeId == employeeId))
            .OrderBy(l => l.PunchedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceLog>> GetUnassignedAsync(string deviceUserId, CancellationToken cancellationToken = default) =>
        await db.AttendanceLogs.Where(l => l.EmployeeId == null && l.DeviceUserId == deviceUserId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> CountUnassignedAsync(CancellationToken cancellationToken = default) =>
        await db.AttendanceLogs.Where(l => l.EmployeeId == null && l.DeviceUserId != null)
            .GroupBy(l => l.DeviceUserId!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
}

internal sealed class DeviceCommandRepository(HrDbContext db) : IDeviceCommandRepository
{
    public Task<DeviceCommand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.DeviceCommands.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(DeviceCommand aggregate) => db.DeviceCommands.Add(aggregate);

    public Task<DeviceCommand?> GetNextAsync(string serialNumber, CancellationToken cancellationToken = default) =>
        db.DeviceCommands.Where(c => c.DeviceSerialNumber == serialNumber && c.SentAtUtc == null).OrderBy(c => c.Number).FirstOrDefaultAsync(cancellationToken);

    public Task<DeviceCommand?> GetByNumberAsync(string serialNumber, int number, CancellationToken cancellationToken = default) =>
        db.DeviceCommands.SingleOrDefaultAsync(c => c.DeviceSerialNumber == serialNumber && c.Number == number, cancellationToken);

    public async Task<int> GetLastNumberAsync(string serialNumber, CancellationToken cancellationToken = default) =>
        await db.DeviceCommands.Where(c => c.DeviceSerialNumber == serialNumber).MaxAsync(c => (int?)c.Number, cancellationToken) ?? 0;

    public async Task<IReadOnlyList<DeviceCommand>> GetRecentAsync(int perDevice, CancellationToken cancellationToken = default) =>
        (await db.DeviceCommands.AsNoTracking().OrderByDescending(c => c.QueuedAtUtc).Take(200).ToListAsync(cancellationToken))
            .GroupBy(c => c.DeviceSerialNumber)
            .SelectMany(g => g.OrderByDescending(c => c.Number).Take(perDevice))
            .ToList();
}

internal sealed class DeviceUserRepository(HrDbContext db) : IDeviceUserRepository
{
    public Task<DeviceUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.DeviceUsers.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public void Add(DeviceUser aggregate) => db.DeviceUsers.Add(aggregate);

    public async Task<IReadOnlyList<DeviceUser>> GetForDeviceAsync(string serialNumber, CancellationToken cancellationToken = default) =>
        await db.DeviceUsers.Where(u => u.DeviceSerialNumber == serialNumber).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DeviceUser>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.DeviceUsers.AsNoTracking().ToListAsync(cancellationToken);
}

internal sealed class DayMarkRepository(HrDbContext db) : IDayMarkRepository
{
    public async Task<IReadOnlyList<DayMark>> GetForLeaveRequestAsync(Guid leaveRequestId, CancellationToken cancellationToken = default) =>
        await db.DayMarks.Where(m => m.LeaveRequestId == leaveRequestId).OrderBy(m => m.Date).ToListAsync(cancellationToken);

    public Task<DayMark?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.DayMarks.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public void Add(DayMark aggregate) => db.DayMarks.Add(aggregate);

    public void Remove(DayMark mark) => db.DayMarks.Remove(mark);

    public Task<DayMark?> GetAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        db.DayMarks.SingleOrDefaultAsync(m => m.EmployeeId == employeeId && m.Date == date, cancellationToken);

    public async Task<IReadOnlyList<DayMark>> GetBetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await db.DayMarks.AsNoTracking().Where(m => m.Date >= from && m.Date <= to).ToListAsync(cancellationToken);
}

internal sealed class AdministrativePenaltyRepository(HrDbContext db) : IAdministrativePenaltyRepository
{
    public Task<AdministrativePenalty?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AdministrativePenalties.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(AdministrativePenalty aggregate) => db.AdministrativePenalties.Add(aggregate);

    public void Remove(AdministrativePenalty penalty) => db.AdministrativePenalties.Remove(penalty);

    public Task<AdministrativePenalty?> GetAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        db.AdministrativePenalties.SingleOrDefaultAsync(p => p.EmployeeId == employeeId && p.Date == date && p.DisciplinaryActionId == null, cancellationToken);

    public Task<AdministrativePenalty?> GetForDisciplinaryActionAsync(Guid disciplinaryActionId, CancellationToken cancellationToken = default) =>
        db.AdministrativePenalties.SingleOrDefaultAsync(p => p.DisciplinaryActionId == disciplinaryActionId, cancellationToken);

    public async Task<IReadOnlyList<AdministrativePenalty>> GetBetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await db.AdministrativePenalties.AsNoTracking().Where(p => p.Date >= from && p.Date <= to).ToListAsync(cancellationToken);
}

internal sealed class PublicHolidayRepository(HrDbContext db) : IPublicHolidayRepository
{
    public Task<PublicHoliday?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PublicHolidays.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);

    public void Add(PublicHoliday aggregate) => db.PublicHolidays.Add(aggregate);

    public void Remove(PublicHoliday holiday) => db.PublicHolidays.Remove(holiday);

    public async Task<IReadOnlyList<PublicHoliday>> GetBetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await db.PublicHolidays.AsNoTracking()
            .Where(h => h.FirstDay <= to && h.LastDay >= from)
            .OrderBy(h => h.FirstDay)
            .ToListAsync(cancellationToken);
}

internal sealed class DailyRecordRepository(HrDbContext db) : IDailyRecordRepository
{
    public Task<DailyRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.DailyRecords.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(DailyRecord aggregate) => db.DailyRecords.Add(aggregate);

    public async Task<IReadOnlyList<DailyRecord>> GetForEmployeeAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await db.DailyRecords.Where(r => r.EmployeeId == employeeId && r.Date >= from && r.Date <= to).ToListAsync(cancellationToken);
}

internal sealed class PayrollAdjustmentRepository(HrDbContext db) : IPayrollAdjustmentRepository
{
    public Task<PayrollAdjustment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PayrollAdjustments.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(PayrollAdjustment aggregate) => db.PayrollAdjustments.Add(aggregate);

    public async Task<IReadOnlyList<PayrollAdjustment>> GetForMonthAsync(DateOnly month, CancellationToken cancellationToken = default) =>
        await db.PayrollAdjustments.AsNoTracking().Where(a => a.Month == month).ToListAsync(cancellationToken);
}

// Tracked: a share is taken back in place.
internal sealed class AdvanceAllocationRepository(HrDbContext db) : IAdvanceAllocationRepository
{
    public Task<AdvanceAllocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AdvanceAllocations.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(AdvanceAllocation aggregate) => db.AdvanceAllocations.Add(aggregate);

    public async Task<IReadOnlyList<AdvanceAllocation>> GetForAdvancesAsync(
        IReadOnlyCollection<Guid> advanceIds, CancellationToken cancellationToken = default) =>
        await db.AdvanceAllocations
            .Where(a => advanceIds.Contains(a.AdvanceId))
            .OrderBy(a => a.AllocatedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdvanceAllocation>> GetForPayrollMonthAsync(
        DateOnly month, Guid? employeeId = null, CancellationToken cancellationToken = default) =>
        await db.AdvanceAllocations.AsNoTracking().Where(a => a.PayrollMonth == month && (employeeId == null || a.EmployeeId == employeeId))
            .OrderBy(a => a.Date).ThenBy(a => a.AllocatedAtUtc).ToListAsync(cancellationToken);
}

// Tracked: one is voided in place.
internal sealed class HrAdvanceRepository(HrDbContext db) : IHrAdvanceRepository
{
    public Task<HrAdvance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.HrAdvances.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(HrAdvance aggregate) => db.HrAdvances.Add(aggregate);

    public async Task<IReadOnlyList<HrAdvance>> GetForPayrollMonthAsync(
        DateOnly month, Guid? employeeId = null, CancellationToken cancellationToken = default) =>
        await db.HrAdvances.AsNoTracking().Where(a => a.PayrollMonth == month && (employeeId == null || a.EmployeeId == employeeId))
            .OrderBy(a => a.Date).ThenBy(a => a.RecordedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HrAdvance>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        await db.HrAdvances.AsNoTracking().Where(a => a.EmployeeId == null && a.VoidedAtUtc == null)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.RecordedAtUtc).ToListAsync(cancellationToken);
}

// Tracked: one is voided in place.
internal sealed class SalaryPaymentRepository(HrDbContext db) : ISalaryPaymentRepository
{
    public Task<SalaryPayment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.SalaryPayments.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(SalaryPayment aggregate) => db.SalaryPayments.Add(aggregate);

    public async Task<IReadOnlyList<SalaryPayment>> GetForPayrollMonthAsync(
        DateOnly month, Guid? employeeId = null, CancellationToken cancellationToken = default) =>
        await db.SalaryPayments.AsNoTracking().Where(p => p.PayrollMonth == month && (employeeId == null || p.EmployeeId == employeeId))
            .OrderBy(p => p.Date).ThenBy(p => p.RecordedAtUtc).ToListAsync(cancellationToken);
}

// Tracked: a draft is worked out again in place.
internal sealed class PayrollSlipRepository(HrDbContext db) : IPayrollSlipRepository
{
    public Task<PayrollSlip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PayrollSlips.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public void Add(PayrollSlip aggregate) => db.PayrollSlips.Add(aggregate);

    public async Task<PayrollSlip?> GetAsync(Guid employeeId, DateOnly month, CancellationToken cancellationToken = default) =>
        db.PayrollSlips.Local.FirstOrDefault(s => s.EmployeeId == employeeId && s.Month == month)
        ?? await db.PayrollSlips.SingleOrDefaultAsync(s => s.EmployeeId == employeeId && s.Month == month, cancellationToken);

    public async Task<IReadOnlyList<PayrollSlip>> GetForMonthAsync(DateOnly month, CancellationToken cancellationToken = default) =>
        await db.PayrollSlips.Where(s => s.Month == month).ToListAsync(cancellationToken);
}

internal sealed class AttendanceSettingsStore(HrDbContext db) : IAttendanceSettings
{
    public async Task<AttendancePolicy> GetAsync(CancellationToken cancellationToken) =>
        (await db.HrSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken))?.ToPolicy() ?? AttendancePolicy.Default;

    public async Task SaveAsync(AttendancePolicy policy, CancellationToken cancellationToken)
    {
        var settings = await db.HrSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new HrSettings();
            db.HrSettings.Add(settings);
        }

        settings.GraceMinutes = policy.GraceMinutes;
        settings.DoubleScanMinutes = policy.DoubleScanMinutes;
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class LeaveTypeRepository(HrDbContext db) : ILeaveTypeRepository
{
    public Task<LeaveType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.LeaveTypes.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public void Add(LeaveType aggregate) => db.LeaveTypes.Add(aggregate);

    public void Remove(LeaveType type) => db.LeaveTypes.Remove(type);

    public async Task<IReadOnlyList<LeaveType>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.LeaveTypes.AsNoTracking().OrderBy(l => l.Name).ToListAsync(cancellationToken);
}

internal sealed class EmployeeLeaveBalanceRepository(HrDbContext db) : IEmployeeLeaveBalanceRepository
{
    public Task<EmployeeLeaveBalance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.EmployeeLeaveBalances.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

    public void Add(EmployeeLeaveBalance aggregate) => db.EmployeeLeaveBalances.Add(aggregate);

    public Task<EmployeeLeaveBalance?> GetAsync(Guid employeeId, Guid leaveTypeId, int year, CancellationToken cancellationToken = default) =>
        db.EmployeeLeaveBalances.SingleOrDefaultAsync(b => b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId && b.Year == year, cancellationToken);

    public async Task<IReadOnlyList<EmployeeLeaveBalance>> GetForYearAsync(int year, Guid? employeeId = null, CancellationToken cancellationToken = default) =>
        await db.EmployeeLeaveBalances.AsNoTracking()
            .Where(b => b.Year == year && (employeeId == null || b.EmployeeId == employeeId))
            .ToListAsync(cancellationToken);

    public Task<bool> AnyForTypeAsync(Guid leaveTypeId, CancellationToken cancellationToken = default) =>
        db.EmployeeLeaveBalances.AnyAsync(b => b.LeaveTypeId == leaveTypeId, cancellationToken);
}

internal sealed class LeaveRequestRepository(HrDbContext db) : ILeaveRequestRepository
{
    public Task<LeaveRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.LeaveRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(LeaveRequest aggregate) => db.LeaveRequests.Add(aggregate);

    public async Task<IReadOnlyList<LeaveRequest>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        await db.LeaveRequests.AsNoTracking()
            .Where(r => r.Status == LeaveRequestStatus.Pending)
            .OrderBy(r => r.RequestedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LeaveRequest>> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await db.LeaveRequests.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.RequestedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LeaveRequest>> GetForYearAsync(int year, CancellationToken cancellationToken = default) =>
        await db.LeaveRequests.AsNoTracking()
            .Where(r => r.StartDate >= new DateOnly(year, 1, 1) && r.StartDate <= new DateOnly(year, 12, 31))
            .OrderByDescending(r => r.StartDate).ThenByDescending(r => r.RequestedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LeaveRequest>> GetOverlappingAsync(Guid employeeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
        await db.LeaveRequests.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId
                && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved)
                && r.StartDate <= endDate
                && (r.CancelledFrom == null ? r.EndDate : r.CancelledFrom.Value.AddDays(-1)) >= startDate)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyForTypeAsync(Guid leaveTypeId, CancellationToken cancellationToken = default) =>
        db.LeaveRequests.AnyAsync(r => r.LeaveTypeId == leaveTypeId, cancellationToken);
}

internal sealed class PerformanceKpiRepository(HrDbContext db) : IPerformanceKpiRepository
{
    public Task<PerformanceKpi?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PerformanceKpis.SingleOrDefaultAsync(k => k.Id == id, cancellationToken);

    public void Add(PerformanceKpi aggregate) => db.PerformanceKpis.Add(aggregate);

    public void Remove(PerformanceKpi kpi) => db.PerformanceKpis.Remove(kpi);

    public async Task<IReadOnlyList<PerformanceKpi>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.PerformanceKpis.AsNoTracking().OrderBy(k => k.Name).ToListAsync(cancellationToken);
}

internal sealed class PerformanceEvaluationRepository(HrDbContext db) : IPerformanceEvaluationRepository
{
    public Task<PerformanceEvaluation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PerformanceEvaluations.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void Add(PerformanceEvaluation aggregate) => db.PerformanceEvaluations.Add(aggregate);

    public async Task<IReadOnlyList<PerformanceEvaluation>> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await db.PerformanceEvaluations.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.RecordedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyForKpiAsync(Guid kpiId, CancellationToken cancellationToken = default) =>
        db.PerformanceEvaluations.AnyAsync(e => e.Scores.Any(s => s.KpiId == kpiId), cancellationToken);
}

internal sealed class DisciplinaryActionRepository(HrDbContext db) : IDisciplinaryActionRepository
{
    public Task<DisciplinaryAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.DisciplinaryActions.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(DisciplinaryAction aggregate) => db.DisciplinaryActions.Add(aggregate);

    public async Task<IReadOnlyList<DisciplinaryAction>> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await db.DisciplinaryActions.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.IssuedAtUtc)
            .ToListAsync(cancellationToken);
}
