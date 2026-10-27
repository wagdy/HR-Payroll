using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HrPayroll.Api.Auth;
using HrPayroll.Application.Approvals;
using HrPayroll.Domain.Access;

namespace HrPayroll.Api.Controllers;

// The approvals a manager gives (maker-checker): an accountant's change to an employee, an
// advance, an asset or a custody voucher waits here until a manager approves or rejects it.
[ApiController]
[Route("api/approval-requests")]
[Authorize(Policy = Permissions.HrView)]
public sealed class ApprovalsController(ISender sender) : ControllerBase
{
    // What waits for a manager, for the screens to mark and lock; with ?decided=, the last
    // decisions too, for whoever decides them.
    [HttpGet]
    public Task<ApprovalRequestsDto> GetAll([FromQuery] int decided, CancellationToken cancellationToken) =>
        sender.Send(new GetApprovalRequestsQuery(CanApprove ? Math.Clamp(decided, 0, 200) : 0), cancellationToken);

    [HttpPost("{requestId:guid}/approve")]
    [Authorize(Policy = Permissions.ApprovalDecide)]
    public Task<ApprovalRequestDto> Approve(Guid requestId, CancellationToken cancellationToken) =>
        sender.Send(new ApproveRequestCommand(requestId, UserName), cancellationToken);

    [HttpPost("{requestId:guid}/reject")]
    [Authorize(Policy = Permissions.ApprovalDecide)]
    public Task<ApprovalRequestDto> Reject(Guid requestId, RejectRequestBody request, CancellationToken cancellationToken) =>
        sender.Send(new RejectRequestCommand(requestId, request.Note, UserName), cancellationToken);

    private string UserName => User.FindFirst(UserClaims.Name)?.Value ?? string.Empty;

    private bool CanApprove => UserClaims.RoleOf(User) is { } role && RolePermissions.Has(role, Permissions.ApprovalDecide);
}

public sealed record RejectRequestBody(string? Note = null);
