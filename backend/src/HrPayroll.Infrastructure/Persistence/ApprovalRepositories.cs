using Microsoft.EntityFrameworkCore;
using HrPayroll.Domain.Approvals;

namespace HrPayroll.Infrastructure.Persistence;

internal sealed class ApprovalRequestRepository(HrDbContext db) : IApprovalRequestRepository
{
    public Task<ApprovalRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.ApprovalRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(ApprovalRequest aggregate) => db.ApprovalRequests.Add(aggregate);

    public async Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status == ApprovalStatus.Pending)
            .OrderBy(r => r.RequestedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApprovalRequest>> GetDecidedAsync(int take, CancellationToken cancellationToken = default) =>
        await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status != ApprovalStatus.Pending)
            .OrderByDescending(r => r.DecidedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<ApprovalRequest?> GetPendingForAsync(Guid recordId, CancellationToken cancellationToken = default) =>
        db.ApprovalRequests.SingleOrDefaultAsync(r => r.RecordId == recordId && r.Status == ApprovalStatus.Pending, cancellationToken);
}
