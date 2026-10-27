using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HrPayroll.Domain.Attendance;
using HrPayroll.Infrastructure.Persistence;

namespace HrPayroll.Infrastructure.Hr;

// How the attendance device's punches are read (إعدادات الحضور): one row, set by a manager. None
// set yet, no grace for arriving late, and scans within three minutes of each other are one.
public sealed class HrSettings
{
    public const int SingletonId = 1;

    public int Id { get; init; } = SingletonId;
    public int GraceMinutes { get; set; } = AttendancePolicy.Default.GraceMinutes;
    public int DoubleScanMinutes { get; set; } = AttendancePolicy.Default.DoubleScanMinutes;

    public AttendancePolicy ToPolicy() => new(GraceMinutes, DoubleScanMinutes);
}

internal sealed class HrSettingsConfiguration : IEntityTypeConfiguration<HrSettings>
{
    public void Configure(EntityTypeBuilder<HrSettings> builder)
    {
        builder.ToTable("Settings", HrDbContext.HrSchema);
        builder.Property(s => s.Id).ValueGeneratedNever();
    }
}
