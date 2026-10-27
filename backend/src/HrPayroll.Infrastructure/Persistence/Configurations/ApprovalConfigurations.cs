using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HrPayroll.Domain.Approvals;

namespace HrPayroll.Infrastructure.Persistence.Configurations;

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests", HrDbContext.HrSchema);
        builder.Property(r => r.Module).IsEnumName();
        builder.Property(r => r.Action).IsEnumName();
        builder.Property(r => r.Status).IsEnumName();
        builder.Property(r => r.Summary).HasMaxLength(500);
        // What the manager reads, line by line, and what a new or changed record holds until
        // approved: both JSON, read and written whole.
        builder.Ignore(r => r.Changes);
        builder.Property<List<ApprovalChange>>("_changes")
            .HasColumnName("Changes")
            .HasColumnType("jsonb")
            .HasConversion(
                changes => JsonSerializer.Serialize(changes, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<ApprovalChange>>(json, (JsonSerializerOptions?)null) ?? new List<ApprovalChange>(),
                new ValueComparer<List<ApprovalChange>>(
                    (a, b) => a!.SequenceEqual(b!),
                    changes => changes.Aggregate(0, (hash, change) => HashCode.Combine(hash, change.GetHashCode())),
                    changes => changes.ToList()));
        builder.Property(r => r.Payload).HasColumnType("jsonb");
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.RequestedById).HasMaxLength(200);
        builder.Property(r => r.RequestedBy).HasMaxLength(200);
        builder.Property(r => r.DecidedBy).HasMaxLength(200);
        builder.Property(r => r.DecisionNote).HasMaxLength(500);
        builder.Ignore(r => r.IsPending);
        // At most one request waits for a record, whatever it asks: two would race to change it
        // twice. Record ids are Guids, unique across the modules.
        builder.HasIndex(r => r.RecordId).IsUnique().HasFilter("\"Status\" = 'Pending'");
        builder.HasIndex(r => new { r.Status, r.RequestedAtUtc });
    }
}
