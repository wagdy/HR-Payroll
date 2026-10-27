using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HrPayroll.Application.Common;
using HrPayroll.Domain;
using HrPayroll.Domain.Approvals;
using HrPayroll.Domain.Common;
using HrPayroll.Domain.Company;
using HrPayroll.Domain.Employees;

namespace HrPayroll.Application.Tests;

// The application wired as the API wires it, with every port faked in memory. Commands go
// through MediatR exactly as in production.
public sealed class TestHr
{
    private readonly ServiceProvider _services;

    public HrStore Hr { get; } = new();
    public TestClock Clock { get; } = new();
    public TestCompany Company { get; } = new();

    public TestHr()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddHrApplication();

        services.AddSingleton<IUnitOfWork>(new NoUnitOfWork());
        services.AddSingleton<IWorkClock>(Clock);
        services.AddSingleton<ICompanyProfile>(Company);
        services.AddSingleton<ICompanySettingsStore>(Hr.CompanySettings);
        services.AddSingleton<IApprovalRequestRepository>(Hr.Approvals);
        services.AddSingleton<IEmployeeRepository>(Hr.Employees);
        services.AddSingleton<IEmployeeIdPhotoStore>(Hr.Employees);
        services.AddSingleton<IAttendanceDeviceRepository>(Hr.Devices);
        services.AddSingleton<IAttendanceLogRepository>(Hr.Punches);
        services.AddSingleton<IDayMarkRepository>(Hr.Marks);
        services.AddSingleton<IAdministrativePenaltyRepository>(Hr.Penalties);
        services.AddSingleton<IPublicHolidayRepository>(Hr.Holidays);
        services.AddSingleton<IDeviceCommandRepository>(Hr.Commands);
        services.AddSingleton<IDeviceUserRepository>(Hr.DeviceUsers);
        services.AddSingleton<IDepartmentRepository>(Hr.Departments);
        services.AddSingleton<IJobRoleRepository>(Hr.Roles);
        services.AddSingleton<IDailyRecordRepository>(Hr.Records);
        services.AddSingleton<IPayrollAdjustmentRepository>(Hr.Adjustments);
        services.AddSingleton<IPayrollSlipRepository>(Hr.Slips);
        services.AddSingleton<IAdvanceAllocationRepository>(Hr.Allocations);
        services.AddSingleton<IHrAdvanceRepository>(Hr.HrAdvances);
        services.AddSingleton<ISalaryPaymentRepository>(Hr.Payments);
        services.AddSingleton<IAssetCategoryRepository>(Hr.AssetCategories);
        services.AddSingleton<IStaffHrAccessRepository>(Hr.StaffHrAccess);
        services.AddSingleton<ILeaveTypeRepository>(Hr.LeaveTypes);
        services.AddSingleton<IEmployeeLeaveBalanceRepository>(Hr.LeaveBalances);
        services.AddSingleton<ILeaveRequestRepository>(Hr.LeaveRequests);
        services.AddSingleton<IPerformanceKpiRepository>(Hr.Kpis);
        services.AddSingleton<IPerformanceEvaluationRepository>(Hr.Evaluations);
        services.AddSingleton<IDisciplinaryActionRepository>(Hr.Disciplinary);
        services.AddSingleton<IAssetItemRepository>(Hr.Assets);
        services.AddSingleton<IAssetTransactionRepository>(Hr.AssetVouchers);
        services.AddSingleton<Hr.IPinHasher>(Hr.Pins);
        services.AddSingleton<Hr.IAttendanceSettings>(Hr.Settings);
        services.AddSingleton<Hr.IAttendanceNotifier>(Hr.Notifications);
        services.AddSingleton<Hr.IAttendanceDeviceLink>(Hr.Link);

        // Every handler resolvable: a port left out fails here, not in one test.
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    // One request per scope, like one HTTP request.
    public async Task<T> Send<T>(IRequest<T> request)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public async Task Send(IRequest request)
    {
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    // The fakes keep what is added at once.
    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}

// The company a test runs for: in Egypt, its money written "L.E 1,400.00", Friday its weekend.
public sealed class TestCompany : ICompanyProfile
{
    public CompanyProfile Current { get; private set; } = new(
        "Acme", new MoneyFormat("L.E", CurrencyPosition.Before), Country.Egypt, "Africa/Cairo", 4, WeekDays.Friday, HasLogo: false, LogoUpdatedAtUtc: null);

    public void Set(CompanyProfile profile) => Current = profile;
}

// The workday a test sets; dates as they fall.
public sealed class TestClock : IWorkClock
{
    public DateOnly Today { get; set; } = new(2026, 11, 3);

    public DateOnly WorkdayOf(DateTime utc) => Today;

    public DateOnly LocalDateOf(DateTime utc) => Today;

    public DateTime LocalTimeOf(DateTime utc) => Today.ToDateTime(TimeOnly.FromDateTime(utc));
}
