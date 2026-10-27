using HrPayroll.Domain.Common;
using HrPayroll.Domain.Approvals;

namespace HrPayroll.Domain.Tests;

// Maker-checker: an accountant asks, a manager decides, once.
public class ApprovalTests
{
    [Fact]
    public void A_change_is_asked_for_with_what_it_holds_and_waits_for_a_manager()
    {
        Assert.Throws<DomainException>(() => ApprovalRequest.AskToChange(
            Guid.NewGuid(), ApprovalModule.Employee, ApprovalAction.Update, Guid.NewGuid(), "Hassan", [], " ", null, "user-1", "Mona"));
        Assert.Throws<DomainException>(() => ApprovalRequest.AskToChange(
            Guid.NewGuid(), ApprovalModule.Advance, ApprovalAction.Create, Guid.NewGuid(), " ", [], "{}", null, "user-1", "Mona"));
        Assert.Throws<DomainException>(() => ApprovalRequest.AskToChange(
            Guid.NewGuid(), (ApprovalModule)99, ApprovalAction.Create, Guid.NewGuid(), "Something", [], "{}", null, "user-1", "Mona"));

        var asked = ApprovalRequest.AskToChange(
            Guid.NewGuid(), ApprovalModule.Employee, ApprovalAction.Update, Guid.NewGuid(), "Employee changed · Hassan",
            [new ApprovalChange("Monthly salary", "5,000.00", "5,500.00")], "{\"monthlySalary\":5500}", "  Promotion ", "user-1", "Mona");
        Assert.Equal((ApprovalAction.Update, ApprovalStatus.Pending, "5,500.00", "Promotion", true),
            (asked.Action, asked.Status, asked.Changes.Single().After, asked.Reason, asked.IsPending));
    }

    [Fact]
    public void A_request_is_decided_once()
    {
        ApprovalRequest Ask() => ApprovalRequest.AskToChange(
            Guid.NewGuid(), ApprovalModule.Custody, ApprovalAction.Create, Guid.NewGuid(), "New asset · Laptop × 1", [], "{}", "New starter", "user-1", "Sara");

        var rejected = Ask();
        rejected.Reject("Omar", "  Buy it next month ");
        rejected.Reject("Omar", null);
        Assert.Equal((ApprovalStatus.Rejected, "Omar", "Buy it next month"), (rejected.Status, rejected.DecidedBy, rejected.DecisionNote));
        Assert.Throws<DomainException>(() => rejected.Approve("Omar"));

        var approved = Ask();
        approved.Approve("Omar");
        approved.Approve("Omar");
        Assert.Equal((ApprovalStatus.Approved, "Omar", false), (approved.Status, approved.DecidedBy, approved.IsPending));
        Assert.Throws<DomainException>(() => approved.Reject("Omar", null));
    }
}
