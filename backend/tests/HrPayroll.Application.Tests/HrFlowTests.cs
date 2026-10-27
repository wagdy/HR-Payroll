using HrPayroll.Application.Approvals;
using HrPayroll.Application.Custody;
using HrPayroll.Application.Hr;
using HrPayroll.Domain.Approvals;
using HrPayroll.Domain.Common;
using HrPayroll.Domain.Custody;
using HrPayroll.Domain.Employees;
using HrPayroll.Domain.Payroll;

namespace HrPayroll.Application.Tests;

// The flows HR's records make on their own, no safe behind them: advances recorded,
// given out and deducted; a month's pay finalized and paid; an asset broken.
public class HrFlowTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    private static Employee Hassan(TestHr hr)
    {
        var hassan = new Employee(Guid.NewGuid(), "Hassan Ali", "Technician", 5_400, new TimeOnly(9, 0), WeekDays.Friday, new DateOnly(2026, 1, 1));
        hr.Hr.Employees.Add(hassan);
        return hassan;
    }

    [Fact]
    public async Task An_accountants_advance_waits_for_a_manager_and_once_approved_comes_off_the_pay()
    {
        var hr = new TestHr();
        var hassan = Hassan(hr);
        var input = new HrAdvanceInput(500, new DateOnly(2026, 10, 20), hassan.Id, "Rent");

        var asked = await hr.Send(new RecordHrAdvanceCommand(Guid.NewGuid(), Guid.NewGuid(), input, "user-2", "Sara", CanApprove: false));
        Assert.Equal((ApprovalOutcome.Requested, "Advance · Hassan Ali · 500.00"), (asked.Outcome, asked.Request!.Summary));
        Assert.Empty(hr.Hr.HrAdvances.All);

        await hr.Send(new ApproveRequestCommand(asked.Request.Id, "Omar"));
        var advance = Assert.Single(hr.Hr.HrAdvances.All);
        Assert.Equal((500m, (Guid?)hassan.Id, (DateOnly?)October, "Sara", "Omar"),
            (advance.Amount, advance.EmployeeId, advance.PayrollMonth, advance.RecordedBy, advance.ApprovedBy));

        var month = await hr.Send(new GetAdvancesQuery(October));
        var line = Assert.Single(month.Allocated);
        Assert.Equal((AdvanceOrigin.Direct, 500m, "Hassan Ali"), (line.Origin, line.Amount, line.Employee));
        var slip = Assert.Single((await hr.Send(new GetPayrollQuery(October))).Slips);
        Assert.Equal(500m, slip.Figures.Advances);
        Assert.Equal((500m, false), (Assert.Single(slip.Advances).Amount, slip.Advances[0].IsShare));
    }

    [Fact]
    public async Task A_lump_sum_is_given_out_a_share_each_and_each_share_comes_off_its_employees_pay()
    {
        var hr = new TestHr();
        var hassan = Hassan(hr);
        var lumpId = Guid.NewGuid();
        await hr.Send(new RecordHrAdvanceCommand(lumpId, Guid.NewGuid(), new HrAdvanceInput(1_000, new DateOnly(2026, 10, 12), null, "Operations"), "user-1", "Omar", CanApprove: true));

        var waiting = Assert.Single((await hr.Send(new GetAdvancesQuery(October))).Pending);
        Assert.Equal((1_000m, 1_000m), (waiting.Amount, waiting.Remaining));

        var given = await hr.Send(new DistributeAdvanceCommand(lumpId, [new AdvanceShareRequest(hassan.Id, 400)], "Omar"));
        Assert.Equal((400m, 600m), (given.Allocated, given.Remaining));
        var refused = await Assert.ThrowsAsync<DomainException>(() => hr.Send(new DistributeAdvanceCommand(lumpId, [new AdvanceShareRequest(hassan.Id, 700)], "Omar")));
        Assert.Contains("600.00 of these advances is left", refused.Message);

        var slip = Assert.Single((await hr.Send(new GetPayrollQuery(October))).Slips);
        Assert.Equal((400m, true), (slip.Figures.Advances, Assert.Single(slip.Advances).IsShare));
    }

    [Fact]
    public async Task A_finalized_months_pay_is_paid_never_past_what_it_owes_and_a_payment_is_voided()
    {
        var hr = new TestHr();
        var hassan = Hassan(hr);
        await hr.Send(new AddPayrollAdjustmentCommand(Guid.NewGuid(), hassan.Id, October, AdjustmentKind.Incentive, 2_000, "Target met", null, "Omar"));

        var early = await Assert.ThrowsAsync<DomainException>(() =>
            hr.Send(new RecordSalaryPaymentCommand(Guid.NewGuid(), hassan.Id, October, new DateOnly(2026, 11, 1), 100, null, "Omar")));
        Assert.Contains("once it is finalized", early.Message);

        var slip = await hr.Send(new FinalizePayrollSlipCommand(October, hassan.Id, "Omar"));
        var net = slip.Figures.NetPayable;
        Assert.True(net > 0);

        var paid = await hr.Send(new RecordSalaryPaymentCommand(Guid.NewGuid(), hassan.Id, October, new DateOnly(2026, 11, 1), net - 100, "Transfer", "Omar"));
        await Assert.ThrowsAsync<DomainException>(() =>
            hr.Send(new RecordSalaryPaymentCommand(Guid.NewGuid(), hassan.Id, October, new DateOnly(2026, 11, 2), 101, null, "Omar")));
        var after = Assert.Single((await hr.Send(new GetPayrollQuery(October))).Slips);
        Assert.Equal((net - 100, 100m), (after.SalaryPaid, after.SalaryOwed));

        await hr.Send(new VoidSalaryPaymentCommand(paid.Id, "Omar"));
        after = Assert.Single((await hr.Send(new GetPayrollQuery(October))).Slips);
        Assert.Equal((0m, net), (after.SalaryPaid, after.SalaryOwed));
        Assert.NotNull(Assert.Single(after.Salaries).VoidedAtUtc);
    }

    [Fact]
    public async Task An_asset_broken_is_gone_from_it_and_nothing_else_is_made()
    {
        var hr = new TestHr();
        var hassan = Hassan(hr);
        var asset = Guid.NewGuid();
        await hr.Send(new ChangeCustodyCommand(asset, Guid.NewGuid(), new CustodyRequest(Asset: new AssetInput("Office chairs", null, 1_200, 4, null)), null, "user-1", "Omar", CanApprove: true));
        await hr.Send(new ChangeCustodyCommand(asset, Guid.NewGuid(), new CustodyRequest(CheckIn: new CheckInInput(Guid.NewGuid(), hassan.Id, new DateOnly(2026, 11, 1), null)), null, "user-1", "Omar", CanApprove: true));

        var broken = await hr.Send(new ChangeCustodyCommand(
            asset, Guid.NewGuid(), new CustodyRequest(CheckOut: new CheckOutInput(Guid.NewGuid(), AssetMovementReason.Breakage, 4, new DateOnly(2026, 11, 2), "Worn out")),
            null, "user-1", "Omar", CanApprove: true));
        Assert.Equal((AssetStatus.Broken, 0), (broken.Asset!.Status, broken.Asset.Quantity));
        Assert.Equal((AssetMovementReason?)AssetMovementReason.Breakage, broken.Voucher!.Reason);
        Assert.Equal(2, hr.Hr.AssetVouchers.All.Count);
    }

    [Fact]
    public async Task An_accountants_custody_change_waits_and_the_asset_is_locked_meanwhile()
    {
        var hr = new TestHr();
        var asset = Guid.NewGuid();
        var asked = await hr.Send(new ChangeCustodyCommand(
            asset, Guid.NewGuid(), new CustodyRequest(Asset: new AssetInput("Laptop", null, 25_000, 1, null)), "New starter", "user-2", "Sara", CanApprove: false));
        Assert.Equal((ApprovalOutcome.Requested, ApprovalModule.Custody), (asked.Outcome, asked.Request!.Module));
        Assert.Empty(hr.Hr.Assets.All);
        await Assert.ThrowsAsync<DomainException>(() => hr.Send(new ChangeCustodyCommand(
            asset, Guid.NewGuid(), new CustodyRequest(Asset: new AssetInput("Laptop", null, 20_000, 1, null)), null, "user-2", "Sara", CanApprove: false)));

        await hr.Send(new RejectRequestCommand(asked.Request.Id, "Next month", "Omar"));
        Assert.Empty(hr.Hr.Assets.All);
        Assert.Equal(ApprovalStatus.Rejected, Assert.Single((await hr.Send(new GetApprovalRequestsQuery(10))).Decided).Status);
    }
}
