using Microsoft.Extensions.DependencyInjection;

namespace HrPayroll.Application;

public static class DependencyInjection
{
    // Infrastructure supplies the repositories, the unit of work, the clock and the devices.
    public static IServiceCollection AddHrApplication(this IServiceCollection services)
    {
        services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddScoped<Hr.EmployeeWriter>();
        services.AddScoped<Hr.Schedules>();
        services.AddScoped<Hr.Timesheets>();
        services.AddScoped<Hr.PayrollWorker>();
        services.AddScoped<Hr.DeviceIntake>();
        services.AddScoped<Hr.Presence>();
        services.AddScoped<Hr.AttendanceBroadcast>();
        services.AddScoped<Hr.HrAdvanceWriter>();
        services.AddScoped<Custody.CustodyWriter>();
        return services;
    }
}
