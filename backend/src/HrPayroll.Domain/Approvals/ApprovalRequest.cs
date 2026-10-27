using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Approvals;

// The records a manager decides about. Numbered as in Otantik POS, where HR began, so its
// requests can be brought over as they are.
public enum ApprovalModule
{
    // An employee added, changed, leaving or coming back.
    Employee = 10,

    // An advance, recorded in HR and deducted from pay.
    Advance = 11,

    // Custody & Assets: an asset added or changed, or a custody voucher (in or out).
    Custody = 12,
}

public enum ApprovalAction
{
    Create = 1,
    Update = 2,
}

public enum ApprovalStatus
{
    // Waiting for a manager. The record is as it was (a new one does not exist yet), counts as
    // before, and cannot be changed.
    Pending = 1,

    // Done: added or changed.
    Approved = 2,

    // Left as it was.
    Rejected = 3,
}

// One line of what a request changes, as the manager reads it: "Monthly salary: 5,000 → 5,500".
// Before is empty for something new, After for something taken away.
public sealed record ApprovalChange(string Label, string? Before, string? After);

// One change asked for and decided: the maker-checker record. Someone without approval rights
// (an accountant) asks; nothing changes while it waits; a manager approves (and it is done) or
// rejects (and it is not). Kept either way, as the history of who asked for what and who decided.
//
// It holds what was entered (Payload, as the module writes it), applied on approval and only
// then: a new record does not exist until it is approved, and a changed one stays as it was.
//
// Its id is made by the app, so a retried request is recorded once.
public sealed class ApprovalRequest : AggregateRoot
{
    private List<ApprovalChange> _changes = [];

    private ApprovalRequest() { }

    private ApprovalRequest(
        Guid id,
        ApprovalModule module,
        ApprovalAction action,
        Guid recordId,
        string summary,
        IReadOnlyList<ApprovalChange> changes,
        string? payload,
        string? reason,
        string requestedById,
        string requestedBy)
    {
        if (!Enum.IsDefined(module) || !Enum.IsDefined(action))
            throw new DomainException("Say what is to be done.");
        if (string.IsNullOrWhiteSpace(payload))
            throw new DomainException("Say what the change is.");
        if (string.IsNullOrWhiteSpace(summary))
            throw new DomainException("Describe what the request is about.");
        if (reason?.Trim().Length > 500)
            throw new DomainException("Keep the reason under 500 characters.");

        Id = id;
        Module = module;
        Action = action;
        RecordId = recordId;
        Summary = summary.Trim();
        _changes = [.. changes];
        Payload = payload;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        RequestedById = requestedById;
        RequestedBy = requestedBy;
        RequestedAtUtc = DateTime.UtcNow;
        Status = ApprovalStatus.Pending;
    }

    // A new or changed record, asked for, with what was entered.
    public static ApprovalRequest AskToChange(
        Guid id,
        ApprovalModule module,
        ApprovalAction action,
        Guid recordId,
        string summary,
        IReadOnlyList<ApprovalChange> changes,
        string payload,
        string? reason,
        string requestedById,
        string requestedBy) =>
        new(id, module, action, recordId, summary, changes, payload, reason, requestedById, requestedBy);

    public ApprovalModule Module { get; private set; }
    public ApprovalAction Action { get; private set; }

    // The record's id in its module: the employee, advance, asset or voucher. For a new one, the
    // id it is given when approved.
    public Guid RecordId { get; private set; }

    // What it was when asked about ("Advance · Hassan · 500.00"), so the decision is made on
    // what was asked and the history reads after the record is gone.
    public string Summary { get; private set; } = string.Empty;

    // What a new or changed record would be, field by field, against what it is now.
    public IReadOnlyList<ApprovalChange> Changes => _changes.AsReadOnly();

    // What was entered, as the module reads it back on approval.
    public string? Payload { get; private set; }

    public string? Reason { get; private set; }

    public string RequestedById { get; private set; } = string.Empty;
    public string RequestedBy { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }

    public ApprovalStatus Status { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }

    // Why a rejection was made, for the one who asked.
    public string? DecisionNote { get; private set; }

    public bool IsPending => Status == ApprovalStatus.Pending;

    // The caller does what was asked in the same unit of work.
    public void Approve(string decidedBy)
    {
        if (Status == ApprovalStatus.Approved)
            return;
        if (Status != ApprovalStatus.Pending)
            throw new DomainException("This request was rejected; ask again.");
        Status = ApprovalStatus.Approved;
        DecidedBy = decidedBy;
        DecidedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string decidedBy, string? note)
    {
        if (Status == ApprovalStatus.Rejected)
            return;
        if (Status != ApprovalStatus.Pending)
            throw new DomainException("This has already been approved.");
        if (note?.Trim().Length > 500)
            throw new DomainException("Keep the note under 500 characters.");
        Status = ApprovalStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAtUtc = DateTime.UtcNow;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}

public interface IApprovalRequestRepository : IRepository<ApprovalRequest>
{
    // Waiting for a decision, oldest first.
    Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken = default);

    // Decided, newest first.
    Task<IReadOnlyList<ApprovalRequest>> GetDecidedAsync(int take, CancellationToken cancellationToken = default);

    // The one waiting for this record, whatever it asks: at most one does.
    Task<ApprovalRequest?> GetPendingForAsync(Guid recordId, CancellationToken cancellationToken = default);
}
