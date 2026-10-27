namespace HrPayroll.Domain.Access;

// The system's users (not its employees, who have the self-service portal): who runs HR.
// A manager does everything and decides what an accountant asks; an accountant sees and enters,
// their changes waiting for a manager's approval, in the HR tabs a manager gives them.
public enum UserRole
{
    Manager = 1,
    Accountant = 2,
}

// What a user may do, as the API's policies and the app's buttons read it.
public static class Permissions
{
    // HR & Payroll and Custody & Assets, seen.
    public const string HrView = "hr.view";

    // Changed: an accountant's asked of a manager where a manager decides.
    public const string HrManage = "hr.manage";

    // A manager's: approving and rejecting requests, and what only a manager does.
    public const string ApprovalDecide = "approval.decide";

    // The system's users: added, changed, deactivated, their passwords reset.
    public const string UsersManage = "users.manage";

    public static IReadOnlyList<string> All { get; } = [HrView, HrManage, ApprovalDecide, UsersManage];
}

// What each role may do. Fail closed: a role missing here gets nothing.
public static class RolePermissions
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static readonly IReadOnlyDictionary<UserRole, IReadOnlySet<string>> ByRole =
        new Dictionary<UserRole, IReadOnlySet<string>>
        {
            [UserRole.Manager] = new HashSet<string>(Permissions.All),
            [UserRole.Accountant] = new HashSet<string> { Permissions.HrView, Permissions.HrManage },
        };

    public static IReadOnlySet<string> For(UserRole role) => ByRole.TryGetValue(role, out var permissions) ? permissions : None;

    public static bool Has(UserRole role, string permission) => For(role).Contains(permission);
}
