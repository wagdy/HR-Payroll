using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HrPayroll.Infrastructure.Persistence;

// Used only by `dotnet ef`, so migrations can be added before the API exists:
//   dotnet ef migrations add <Name> --project backend/src/HrPayroll.Infrastructure --output-dir Persistence/Migrations
// Adding a migration never connects to the database. Applying one reads the connection string
// from HRPAYROLL_CONNECTION.
internal sealed class HrDbContextFactory : IDesignTimeDbContextFactory<HrDbContext>
{
    public HrDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HRPAYROLL_CONNECTION")
            ?? "Host=localhost;Port=5435;Database=hr_payroll;Username=hr;Password=hr-dev";

        return new HrDbContext(new DbContextOptionsBuilder<HrDbContext>().UseNpgsql(connectionString).Options);
    }
}
