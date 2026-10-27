using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using HrPayroll.Api.Auth;
using HrPayroll.Application.Hr;
using HrPayroll.Domain.Access;

namespace HrPayroll.Api.Hubs;

// Keeps the HR screens live: as punches come from a device, or a day is put right, whoever sees
// HR is told who is in now, each card in place. Push only, and not replayed: a screen that was
// away reads again.
[Authorize]
public sealed class HrHub : Hub<IHrClient>
{
    public const string Path = "/hubs/hr";

    // Whoever sees HR.
    internal const string Attendance = "attendance";

    public override async Task OnConnectedAsync()
    {
        if (UserClaims.RoleOf(Context.User!) is { } role && RolePermissions.Has(role, Permissions.HrView))
            await Groups.AddToGroupAsync(Context.ConnectionId, Attendance);
        await base.OnConnectedAsync();
    }
}

public interface IHrClient
{
    // Punches came from a device, or a day was put right: the employees it touched, each card on
    // who is in now as it stands, to whoever sees HR.
    Task EmployeeStatusChanged(IReadOnlyList<EmployeeStatusDto> statuses);
}

// HR's IAttendanceNotifier: to whoever sees HR, after punches are saved.
internal sealed class AttendanceNotifier(IHubContext<HrHub, IHrClient> hub, ILogger<AttendanceNotifier> logger) : IAttendanceNotifier
{
    // Never throws: the punches are saved, and a screen that missed it reads again on its minute.
    public async Task StatusChangedAsync(IReadOnlyList<EmployeeStatusDto> statuses, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(HrHub.Attendance).EmployeeStatusChanged(statuses);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not tell the HR screens about new punches; they read again within a minute");
        }
    }
}
