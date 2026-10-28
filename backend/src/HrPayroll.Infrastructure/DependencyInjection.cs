using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HrPayroll.Application.Common;
using HrPayroll.Domain;
using HrPayroll.Domain.Approvals;
using HrPayroll.Domain.Common;
using HrPayroll.Infrastructure.Common;
using HrPayroll.Infrastructure.Hr;
using HrPayroll.Infrastructure.Identity;
using HrPayroll.Infrastructure.Persistence;

namespace HrPayroll.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "HrPayroll";

    // Every port of the application, except the live notifications, which come from the API's hub.
    public static IServiceCollection AddHrInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // DATABASE_URL first (a hosting platform's), else ConnectionStrings:HrPayroll.
        var connectionString = DatabaseConnection.Resolve(configuration);

        services.Configure<CompanyOptions>(configuration.GetSection(CompanyOptions.Section));
        services.AddSingleton<ICompanyProfile, CompanyProfileStore>();
        services.AddSingleton<WorkClock>();
        services.AddSingleton<IWorkClock>(sp => sp.GetRequiredService<WorkClock>());
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<HrDbContext>(o => o.UseNpgsql(connectionString, HrDbContext.ConfigureNpgsql));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HrDbContext>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryOnConflictBehavior<,>));

        // Company Settings.
        services.AddScoped<ICompanySettingsStore, Company.CompanySettingsStore>();

        // Approvals.
        services.AddScoped<IApprovalRequestRepository, ApprovalRequestRepository>();

        // HR & Payroll.
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IEmployeeIdPhotoStore, EmployeeIdPhotoStore>();
        services.AddScoped<IStaffHrAccessRepository, StaffHrAccessRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IJobRoleRepository, JobRoleRepository>();
        services.AddScoped<IAttendanceDeviceRepository, AttendanceDeviceRepository>();
        services.AddScoped<IAttendanceLogRepository, AttendanceLogRepository>();
        services.AddScoped<IDayMarkRepository, DayMarkRepository>();
        services.AddScoped<IAdministrativePenaltyRepository, AdministrativePenaltyRepository>();
        services.AddScoped<IPublicHolidayRepository, PublicHolidayRepository>();
        services.AddScoped<IDeviceCommandRepository, DeviceCommandRepository>();
        services.AddScoped<IDeviceUserRepository, DeviceUserRepository>();
        services.AddScoped<IDailyRecordRepository, DailyRecordRepository>();
        services.AddScoped<IPayrollAdjustmentRepository, PayrollAdjustmentRepository>();
        services.AddScoped<IAdvanceAllocationRepository, AdvanceAllocationRepository>();
        services.AddScoped<IHrAdvanceRepository, HrAdvanceRepository>();
        services.AddScoped<ISalaryPaymentRepository, SalaryPaymentRepository>();
        services.AddScoped<IPayrollSlipRepository, PayrollSlipRepository>();
        services.AddScoped<ILeaveTypeRepository, LeaveTypeRepository>();
        services.AddScoped<IEmployeeLeaveBalanceRepository, EmployeeLeaveBalanceRepository>();
        services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
        services.AddScoped<IPerformanceKpiRepository, PerformanceKpiRepository>();
        services.AddScoped<IPerformanceEvaluationRepository, PerformanceEvaluationRepository>();
        services.AddScoped<IDisciplinaryActionRepository, DisciplinaryActionRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IRosterRepository, RosterRepository>();
        services.AddScoped<HrPayroll.Application.Hr.IAttendanceSettings, AttendanceSettingsStore>();
        services.AddSingleton<HrPayroll.Application.Hr.IPinHasher, PinHasher>();
        services.AddSingleton<HrPayroll.Application.Hr.IAttendanceDeviceLink, Hr.ZkTeco.ZkDeviceLink>();

        // Custody & Assets.
        services.AddScoped<IAssetCategoryRepository, AssetCategoryRepository>();
        services.AddScoped<IAssetItemRepository, AssetItemRepository>();
        services.AddScoped<IAssetTransactionRepository, AssetTransactionRepository>();
        services.AddScoped<IConsumableItemRepository, ConsumableItemRepository>();
        services.AddScoped<IConsumableMovementRepository, ConsumableMovementRepository>();

        // The system's users.
        services.AddScoped<UserDirectory>();

        return services;
    }
}
