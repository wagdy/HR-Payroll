using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Npgsql;
using HrPayroll.Domain.Common;
using HrPayroll.Domain.Access;
using HrPayroll.Domain.Approvals;
using HrPayroll.Domain.Attendance;
using HrPayroll.Domain.Custody;
using HrPayroll.Domain.Employees;
using HrPayroll.Domain.Leaves;
using HrPayroll.Domain.Payroll;
using HrPayroll.Domain.Performance;
using HrPayroll.Infrastructure.Hr;
using HrPayroll.Infrastructure.Identity;

namespace HrPayroll.Infrastructure.Persistence;

// The HR & Assets database: employees and everything about them (attendance, leaves,
// performance, pay), the assets in their custody, the approvals a manager gives, and the
// system's users.
public sealed class HrDbContext(DbContextOptions<HrDbContext> options) : DbContext(options), IUnitOfWork
{
    // HR & Payroll, Custody & Assets and the approvals in one schema; the system's users apart.
    public const string HrSchema = "hr";
    public const string IdentitySchema = "identity";

    // The app's and the design-time tools' own setup. The history of migrations is kept in the hr
    // schema by name: left to the search path, a database user named hr (whose "$user" schema is
    // hr once the first migration makes it) would look there, find none, and apply them again.
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) => npgsql
        .UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery)
        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, HrSchema);

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeIdPhoto> EmployeeIdPhotos => Set<EmployeeIdPhoto>();
    public DbSet<AttendanceDevice> AttendanceDevices => Set<AttendanceDevice>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<DayMark> DayMarks => Set<DayMark>();
    public DbSet<AdministrativePenalty> AdministrativePenalties => Set<AdministrativePenalty>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();
    public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();
    public DbSet<DeviceUser> DeviceUsers => Set<DeviceUser>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobRole> JobRoles => Set<JobRole>();
    public DbSet<DailyRecord> DailyRecords => Set<DailyRecord>();
    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
    public DbSet<AdvanceAllocation> AdvanceAllocations => Set<AdvanceAllocation>();
    public DbSet<HrAdvance> HrAdvances => Set<HrAdvance>();
    public DbSet<SalaryPayment> SalaryPayments => Set<SalaryPayment>();
    public DbSet<PayrollSlip> PayrollSlips => Set<PayrollSlip>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<EmployeeLeaveBalance> EmployeeLeaveBalances => Set<EmployeeLeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<PerformanceKpi> PerformanceKpis => Set<PerformanceKpi>();
    public DbSet<PerformanceEvaluation> PerformanceEvaluations => Set<PerformanceEvaluation>();
    public DbSet<DisciplinaryAction> DisciplinaryActions => Set<DisciplinaryAction>();
    public DbSet<StaffHrAccess> StaffHrAccess => Set<StaffHrAccess>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<AssetItem> AssetItems => Set<AssetItem>();
    public DbSet<AssetTransaction> AssetTransactions => Set<AssetTransaction>();
    public DbSet<ConsumableItem> ConsumableItems => Set<ConsumableItem>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<RosterDay> RosterDays => Set<RosterDay>();
    public DbSet<ConsumableMovement> ConsumableMovements => Set<ConsumableMovement>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<HrSettings> HrSettings => Set<HrSettings>();
    public DbSet<Domain.Company.CompanySettings> CompanySettings => Set<Domain.Company.CompanySettings>();
    public DbSet<Company.CompanyLogo> CompanyLogos => Set<Company.CompanyLogo>();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HrDbContext).Assembly);

        // The domain's aggregates: ids generated in the domain, domain events never stored, and
        // optimistic concurrency on PostgreSQL's xmin.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.Property(nameof(Entity.Id)).ValueGeneratedNever();

            if (typeof(AggregateRoot).IsAssignableFrom(entityType.ClrType))
            {
                entity.Ignore(nameof(AggregateRoot.DomainEvents));
                entity.Property<uint>("Version").IsRowVersion();
            }
        }
    }

    // Money, to two decimal places.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        TouchRootsOfChangedChildren();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (Exception ex) when (ToConflict(ex) is { } conflict)
        {
            throw conflict;
        }
    }

    // The xmin check guards a row, but an aggregate can span several: an evaluation's scores are
    // rows of their own. Marking the aggregate modified whenever one of its children changes puts
    // its row, and its check, into every save that touches it. Only the owning aggregate,
    // recognised by its collection navigation.
    private void TouchRootsOfChangedChildren()
    {
        var changedChildren = ChangeTracker.Entries()
            .Where(e => e.Entity is Entity and not AggregateRoot
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var child in changedChildren)
        {
            foreach (var foreignKey in child.Metadata.GetForeignKeys()
                         .Where(fk => fk.PrincipalToDependent is not null
                             && typeof(AggregateRoot).IsAssignableFrom(fk.PrincipalEntityType.ClrType)))
            {
                var property = child.Property(foreignKey.Properties[0].Name);
                var rootId = (Guid?)(child.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue);
                var root = ChangeTracker.Entries<AggregateRoot>().FirstOrDefault(r => r.Entity.Id == rootId);
                if (root is { State: EntityState.Unchanged })
                    root.State = EntityState.Modified;
            }
        }
    }

    // The save failures a caller can act on, translated so no layer above this one ever handles
    // an EF Core or Npgsql type.
    private static ConflictException? ToConflict(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException =>
            new ConflictException("Someone else changed this at the same time. Reload it and try again.", ex),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
            new ConflictException("This conflicts with a record that already exists.", ex),
        // Removed while someone else put it to use: a department deleted as an employee joined it.
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } } =>
            new ConflictException("It was put to use at the same time. Reload and try again.", ex),
        _ => null,
    };
}
