using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HrPayroll.Domain.Access;
using HrPayroll.Domain.Custody;
using HrPayroll.Application.Approvals;
using HrPayroll.Application.Custody;
using HrPayroll.Api.Auth;
using HrPayroll.Application.Common;

namespace HrPayroll.Api.Controllers;

// Custody & Assets (عهدة وأصول): the categories, the assets and whose custody each is in, and the
// custody vouchers. Its own tab (HrTabs.Custody): a manager's, an accountant's unless a manager
// ticked theirs without it, and an employee's given it, in the portal. Categories are a
// manager's, or such an employee's (Decide). Assets and vouchers: a manager's at once (200),
// anyone else's asked of a manager (202) and made only when approved. An employee never gets what
// an asset cost (HrPrivacyFilter), and so never changes it: an asset they save keeps its cost.
//
// RequestId and a voucher's TransactionId are made by the app: a retry finds what was asked or made.
[ApiController]
[Route("api/custody")]
[HrTab(HrTabs.Custody)]
[TypeFilter(typeof(HrPrivacyFilter))]
public sealed class CustodyController(ISender sender, IWorkClock calendar, HrCaller caller) : ControllerBase
{
    // ---- Categories ----

    [HttpGet("categories")]
    [HrTab(HrTabs.Custody)]
    public Task<IReadOnlyList<AssetCategoryDto>> GetCategories(CancellationToken cancellationToken) =>
        sender.Send(new GetAssetCategoriesQuery(), cancellationToken);

    [HttpPut("categories/{categoryId:guid}")]
    [HrTab(HrTabs.Custody, Decide = true)]
    public Task<AssetCategoryDto> SaveCategory(Guid categoryId, AssetCategoryRequest request, CancellationToken cancellationToken) =>
        sender.Send(new SaveAssetCategoryCommand(categoryId, request.Name, request.IsConsumable), cancellationToken);

    [HttpDelete("categories/{categoryId:guid}")]
    [HrTab(HrTabs.Custody, Decide = true)]
    public async Task<IActionResult> DeleteCategory(Guid categoryId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteAssetCategoryCommand(categoryId), cancellationToken);
        return NoContent();
    }

    // ---- Assets ----

    [HttpGet("assets")]
    [HrTab(HrTabs.Custody)]
    public Task<IReadOnlyList<AssetItemDto>> GetAssets(CancellationToken cancellationToken) =>
        sender.Send(new GetAssetsQuery(), cancellationToken);

    // An asset with its vouchers, the latest first.
    [HttpGet("assets/{assetId:guid}")]
    [HrTab(HrTabs.Custody)]
    public async Task<ActionResult<AssetDetailDto>> GetAsset(Guid assetId, CancellationToken cancellationToken) =>
        await sender.Send(new GetAssetQuery(assetId), cancellationToken) is { } asset ? asset : NotFound();

    // Added under the id the app made, or changed. How many there are is set
    // when it is added; vouchers change it after.
    [HttpPut("assets/{assetId:guid}")]
    [HrTab(HrTabs.Custody, Manage = true)]
    public async Task<IActionResult> SaveAsset(Guid assetId, SaveAssetRequest request, CancellationToken cancellationToken)
    {
        // An employee is never shown what it cost: none sent keeps the cost it has.
        var unitCost = caller.IsEmployee && request.UnitCost is null
            ? (await sender.Send(new GetAssetQuery(assetId), cancellationToken))?.Asset.UnitCost
            : request.UnitCost;
        return await ChangeAsync(assetId, request.RequestId, new CustodyRequest(Asset: new AssetInput(request.Name, request.CategoryId, unitCost, request.Quantity, request.Notes)),
            request.RequestReason, cancellationToken);
    }

    // إذن دخول: all of it into an employee's custody, today unless a day before is given.
    [HttpPost("assets/{assetId:guid}/check-in")]
    [HrTab(HrTabs.Custody, Manage = true)]
    public Task<IActionResult> CheckIn(Guid assetId, CheckInRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(assetId, request.RequestId, new CustodyRequest(CheckIn: new CheckInInput(
            request.TransactionId, request.EmployeeId, request.Date ?? Today, request.Notes)), request.RequestReason, cancellationToken);

    // إذن خروج, and why: handed back or to maintenance (all of it); broken, lost or thrown away
    // (how many).
    [HttpPost("assets/{assetId:guid}/check-out")]
    [HrTab(HrTabs.Custody, Manage = true)]
    public Task<IActionResult> CheckOut(Guid assetId, CheckOutRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(assetId, request.RequestId, new CustodyRequest(CheckOut: new CheckOutInput(
            request.TransactionId, request.Reason, request.Quantity, request.Date ?? Today, request.Notes)), request.RequestReason, cancellationToken);

    // ---- Vouchers ----

    // The vouchers of the days ?from= to ?to=, the latest first; the last 30 days unless said.
    [HttpGet("vouchers")]
    [HrTab(HrTabs.Custody)]
    public Task<IReadOnlyList<AssetTransactionDto>> GetVouchers([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var last = to ?? Today;
        return sender.Send(new GetAssetTransactionsQuery(from ?? last.AddDays(-29), last), cancellationToken);
    }

    // ---- Consumables inventory (جرد الاستهلاكيات) ----

    [HttpGet("consumables")]
    [HrTab(HrTabs.Custody)]
    public Task<IReadOnlyList<ConsumableItemDto>> GetConsumables(CancellationToken cancellationToken) =>
        sender.Send(new GetConsumablesQuery(), cancellationToken);

    // An item with its movements, the latest first.
    [HttpGet("consumables/{itemId:guid}")]
    [HrTab(HrTabs.Custody)]
    public async Task<ActionResult<ConsumableDetailDto>> GetConsumable(Guid itemId, CancellationToken cancellationToken) =>
        await sender.Send(new GetConsumableQuery(itemId), cancellationToken) is { } item ? item : NotFound();

    // Added under the id the app made, or changed; made at once, by whoever manages on the tab.
    [HttpPut("consumables/{itemId:guid}")]
    [HrTab(HrTabs.Custody, Manage = true)]
    public Task<ConsumableItemDto> SaveConsumable(Guid itemId, SaveConsumableRequest request, CancellationToken cancellationToken) =>
        sender.Send(new SaveConsumableCommand(itemId, request.Name, request.Unit, request.ReorderLevel, request.Notes, StaffName), cancellationToken);

    // Only one nothing was ever recorded of.
    [HttpDelete("consumables/{itemId:guid}")]
    [HrTab(HrTabs.Custody, Decide = true)]
    public async Task<IActionResult> DeleteConsumable(Guid itemId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteConsumableCommand(itemId), cancellationToken);
        return NoContent();
    }

    // Stock received, used up or counted, today unless a day before is given. An employee is never
    // shown what things cost, so what they receive goes at the average.
    [HttpPost("consumables/{itemId:guid}/movements")]
    [HrTab(HrTabs.Custody, Manage = true)]
    public Task<ConsumableMovementResultDto> RecordMovement(Guid itemId, ConsumableMovementRequest request, CancellationToken cancellationToken) =>
        sender.Send(new RecordConsumableMovementCommand(
            itemId, request.MovementId, request.Type, request.Quantity, caller.IsEmployee ? null : request.UnitCost, request.Date ?? Today, request.Notes, StaffName),
            cancellationToken);

    private DateOnly Today => calendar.WorkdayOf(DateTime.UtcNow);

    private async Task<IActionResult> ChangeAsync(Guid assetId, Guid requestId, CustodyRequest change, string? reason, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ChangeCustodyCommand(assetId, requestId, change, reason, UserClaims.IdOf(User) ?? string.Empty, StaffName, CanApprove), cancellationToken);
        return result.Outcome == ApprovalOutcome.Requested ? Accepted(new ApprovalResultDto(result.Outcome, result.Request!)) : Ok(result);
    }

    private string StaffName => User.FindFirst(UserClaims.Name)?.Value ?? string.Empty;

    private bool CanApprove => UserClaims.RoleOf(User) is { } role && RolePermissions.Has(role, Permissions.ApprovalDecide);
}

public sealed record AssetCategoryRequest(string Name, bool IsConsumable);

// RequestReason, on each: why it is asked, for a manager's approval.
public sealed record SaveAssetRequest(
    Guid RequestId, string Name, Guid? CategoryId, decimal? UnitCost, int Quantity, string? Notes = null, string? RequestReason = null);

public sealed record CheckInRequest(Guid RequestId, Guid TransactionId, Guid EmployeeId, DateOnly? Date = null, string? Notes = null, string? RequestReason = null);

// Reason: why it goes out (Returned, Maintenance, Breakage, Lost, Discarded).
public sealed record CheckOutRequest(
    Guid RequestId, Guid TransactionId, AssetMovementReason Reason, int Quantity, DateOnly? Date = null, string? Notes = null, string? RequestReason = null);

public sealed record SaveConsumableRequest(string Name, string Unit, decimal? ReorderLevel = null, string? Notes = null);

// Type: Received (with a UnitCost, or none: at the average), Consumed, or Counted (Quantity: what
// was counted). MovementId is made by the app: a retry finds the movement made.
public sealed record ConsumableMovementRequest(
    Guid MovementId, ConsumableMovementType Type, decimal Quantity, decimal? UnitCost = null, DateOnly? Date = null, string? Notes = null);
