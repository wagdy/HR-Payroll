using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using HrPayroll.Domain.Common;
using HrPayroll.Application.Common;
using HrPayroll.Application.Hr;
using HrPayroll.Domain.Approvals;

namespace HrPayroll.Application.Approvals;

// Maker-checker for HR & Payroll and Custody & Assets. Whoever may decide (ApprovalDecide) acts at
// once; anyone else asks, and nothing changes until a manager approves: an employee added or
// changed, leaving or coming back (Employees.cs), an advance (Advances.cs), and an asset added or
// changed or a custody voucher (Custody.cs). One table holds them all, keyed by the record each is
// about; a record with a request waiting is locked.

public enum ApprovalOutcome
{
    // Asked: waiting for a manager.
    Requested = 1,

    // Done: the asker could approve it.
    Applied = 2,
}

public sealed record ApprovalRequestDto(
    Guid Id,
    ApprovalModule Module,
    ApprovalAction Action,
    Guid RecordId,
    string Summary,
    IReadOnlyList<ApprovalChange> Changes,
    string? Reason,
    string RequestedBy,
    DateTime RequestedAtUtc,
    ApprovalStatus Status,
    string? DecidedBy,
    DateTime? DecidedAtUtc,
    string? DecisionNote)
{
    public static ApprovalRequestDto From(ApprovalRequest r) => new(
        r.Id, r.Module, r.Action, r.RecordId, r.Summary, r.Changes, r.Reason, r.RequestedBy, r.RequestedAtUtc, r.Status, r.DecidedBy,
        r.DecidedAtUtc, r.DecisionNote);
}

public sealed record ApprovalResultDto(ApprovalOutcome Outcome, ApprovalRequestDto Request)
{
    // Requested while it waits; Applied once approved.
    public static ApprovalResultDto Of(ApprovalRequest request) =>
        new(request.IsPending ? ApprovalOutcome.Requested : ApprovalOutcome.Applied, ApprovalRequestDto.From(request));
}

public static class ApprovalLocks
{
    // A record with a request waiting is not changed meanwhile: the manager decides on what was
    // asked about.
    public static async Task EnsureNotPendingAsync(this IApprovalRequestRepository requests, Guid recordId, CancellationToken cancellationToken)
    {
        if (await requests.GetPendingForAsync(recordId, cancellationToken) is { } pending)
            throw new DomainException($"{pending.RequestedBy} has asked {What(pending)}. It can be changed once a manager has decided.");
    }

    // "to change this", "for this".
    internal static string What(ApprovalRequest pending) => pending.Action == ApprovalAction.Update ? "to change this" : "for this";
}

// What a new or changed record holds while it waits, as JSON: enums by name, so the stored
// request reads plainly.
internal static class ApprovalPayload
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string Write<T>(T change) => JsonSerializer.Serialize(change, Options);

    public static T Read<T>(ApprovalRequest request) =>
        JsonSerializer.Deserialize<T>(request.Payload ?? throw new DomainException("This request holds no change to apply."), Options)
        ?? throw new DomainException("This request's change could not be read.");
}

// A manager's yes: what was asked is done (added or changed) and the request closed, together.
// Approving again, after a lost answer, changes nothing. When it can no longer be done (the
// employee has left since), it stays waiting, for the manager to put right or reject.
public sealed record ApproveRequestCommand(Guid RequestId, string StaffName) : IRequest<ApprovalRequestDto>, IRetryOnConflict;

internal sealed class ApproveRequestHandler(
    IApprovalRequestRepository requests,
    EmployeeWriter employees,
    HrAdvanceWriter hrAdvances,
    Custody.CustodyWriter custody,
    IUnitOfWork unitOfWork) : IRequestHandler<ApproveRequestCommand, ApprovalRequestDto>
{
    public async Task<ApprovalRequestDto> Handle(ApproveRequestCommand request, CancellationToken cancellationToken)
    {
        var approval = await requests.GetRequiredAsync(request.RequestId, cancellationToken);
        if (approval.Status == ApprovalStatus.Approved)
            return ApprovalRequestDto.From(approval);

        approval.Approve(request.StaffName);
        switch (approval.Action, approval.Module)
        {
            // An employee added, changed, leaving or coming back, as asked.
            case (_, ApprovalModule.Employee):
                await employees.ApplyAsync(approval.RecordId, ApprovalPayload.Read<EmployeeRequest>(approval), cancellationToken);
                break;

            // An advance, recorded as asked.
            case (ApprovalAction.Create, ApprovalModule.Advance):
                await hrAdvances.ApplyAsync(approval.RecordId, ApprovalPayload.Read<HrAdvanceInput>(approval), approval.RequestedBy, request.StaffName, cancellationToken);
                break;

            // An asset added or changed, or a custody voucher, as asked; by who asked.
            case (_, ApprovalModule.Custody):
                await custody.ApplyAsync(approval.RecordId, ApprovalPayload.Read<Custody.CustodyRequest>(approval), approval.RequestedBy, cancellationToken);
                break;

            default:
                throw new DomainException("This request cannot be approved here.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ApprovalRequestDto.From(approval);
    }
}

// A manager's no: the record stays as it is, unlocked; a new one is never made. The request is
// kept, with the note.
public sealed record RejectRequestCommand(Guid RequestId, string? Note, string StaffName) : IRequest<ApprovalRequestDto>;

internal sealed class RejectRequestHandler(IApprovalRequestRepository requests, IUnitOfWork unitOfWork)
    : IRequestHandler<RejectRequestCommand, ApprovalRequestDto>
{
    public async Task<ApprovalRequestDto> Handle(RejectRequestCommand request, CancellationToken cancellationToken)
    {
        var approval = await requests.GetRequiredAsync(request.RequestId, cancellationToken);
        approval.Reject(request.StaffName, request.Note);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ApprovalRequestDto.From(approval);
    }
}

// What waits for a decision, oldest first; with Decided, also the last decisions, newest first.
public sealed record GetApprovalRequestsQuery(int Decided = 0) : IRequest<ApprovalRequestsDto>;

public sealed record ApprovalRequestsDto(IReadOnlyList<ApprovalRequestDto> Pending, IReadOnlyList<ApprovalRequestDto> Decided);

internal sealed class GetApprovalRequestsHandler(IApprovalRequestRepository requests) : IRequestHandler<GetApprovalRequestsQuery, ApprovalRequestsDto>
{
    public async Task<ApprovalRequestsDto> Handle(GetApprovalRequestsQuery request, CancellationToken cancellationToken) => new(
        (await requests.GetPendingAsync(cancellationToken)).Select(ApprovalRequestDto.From).ToList(),
        request.Decided > 0
            ? (await requests.GetDecidedAsync(request.Decided, cancellationToken)).Select(ApprovalRequestDto.From).ToList()
            : []);
}
