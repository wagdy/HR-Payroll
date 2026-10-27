using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HrPayroll.Api.Auth;
using HrPayroll.Domain.Access;
using HrPayroll.Infrastructure.Common;
using HrPayroll.Infrastructure.Identity;

namespace HrPayroll.Api.Controllers;

// Signing in: a user with their email and password; an employee, in the portal, with their mobile
// number and PIN.
[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserDirectory users, TokenService tokens, WorkClock clock, ISender sender) : ControllerBase
{
    public const string SignInRateLimit = "sign-in";
    public const string EmployeeSignInRateLimit = "employee-sign-in";

    // One answer for an email nobody has and a wrong password, so it does not say whose emails
    // are known.
    [AllowAnonymous]
    [EnableRateLimiting(SignInRateLimit)]
    [HttpPost("sign-in")]
    public async Task<ActionResult<SignInResponse>> SignIn(SignInRequest request, CancellationToken cancellationToken)
    {
        var outcome = await users.SignInAsync(request.Email, request.Password, cancellationToken);
        if (outcome.User is not { } user)
        {
            var detail = outcome.LockedUntilUtc is { } lockedUntil
                ? $"Too many wrong passwords. Try again after {clock.ToLocal(lockedUntil):HH:mm}."
                : "Wrong email or password.";
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed.", detail: detail);
        }

        var (token, expiresAtUtc) = tokens.Issue(user);
        return new SignInResponse(token, expiresAtUtc, Me.For(user.Id, user.FullName, user.Email, user.Role));
    }

    // The employee portal's sign-in: their mobile number and the PIN a manager set. A token with
    // the role Employee, good for the portal's own endpoints and the HR tabs given them.
    [AllowAnonymous]
    [EnableRateLimiting(EmployeeSignInRateLimit)]
    [HttpPost("employee-login")]
    public async Task<ActionResult<EmployeeSignInResponse>> EmployeeLogin(EmployeeLoginRequest request, CancellationToken cancellationToken)
    {
        var outcome = await sender.Send(new HrPayroll.Application.Hr.EmployeeSignInCommand(request.MobileNumber, request.PinCode), cancellationToken);
        if (outcome.Employee is not { } employee)
        {
            var detail = outcome.LockedUntilUtc is { } lockedUntil
                ? $"Too many wrong PINs. Try again after {clock.ToLocal(lockedUntil):HH:mm}."
                : "Wrong mobile number or PIN.";
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed.", detail: detail);
        }

        var (token, expiresAtUtc) = tokens.IssueEmployee(employee.Id, employee.Name, employee.PinSetAtUtc, employee.HrTabs);
        return new EmployeeSignInResponse(token, expiresAtUtc, employee.Id, employee.Name, PortalClaims.Role, HrTabSet.Split(employee.HrTabs));
    }

    // Who is signed in, and what they may do: the permissions come from RolePermissions, so the
    // app shows and hides buttons by the same rules the API enforces. HrTabs: the HR & Payroll
    // tabs they hold, every one for a manager, an accountant's as a manager ticked them.
    [HttpGet("me")]
    public async Task<Me> GetMe([FromServices] HrCaller hr, CancellationToken cancellationToken)
    {
        var id = UserClaims.IdOf(User)!;
        var user = await users.GetAsync(id, cancellationToken);
        return Me.For(id, user?.FullName ?? User.FindFirst(UserClaims.Name)?.Value ?? string.Empty, user?.Email ?? string.Empty, UserClaims.RoleOf(User)!.Value) with
        {
            HrTabs = HrTabSet.Split(await hr.TabsAsync(cancellationToken)),
        };
    }

    // Their own password changed, with the one they have now. Signs them out elsewhere; this
    // answer brings them a new token for here.
    [HttpPost("password")]
    public async Task<SignInResponse> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var id = UserClaims.IdOf(User)!;
        await users.ChangeOwnPasswordAsync(id, request.CurrentPassword, request.NewPassword, cancellationToken);
        var user = (await users.GetAsync(id, cancellationToken))!;
        var (token, expiresAtUtc) = tokens.Issue(user);
        return new SignInResponse(token, expiresAtUtc, Me.For(user.Id, user.FullName, user.Email, user.Role));
    }
}

public sealed record SignInRequest(string? Email, string? Password);

public sealed record SignInResponse(string Token, DateTime ExpiresAtUtc, Me User);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record EmployeeLoginRequest(string? MobileNumber, string? PinCode);

// HrTabs: the HR & Payroll tabs a manager gave them, as the token names them.
public sealed record EmployeeSignInResponse(string Token, DateTime ExpiresAtUtc, Guid EmployeeId, string Name, string Role, IReadOnlyList<HrTabs> HrTabs);

public sealed record Me(string Id, string FullName, string Email, UserRole Role, IReadOnlyList<string> Permissions, IReadOnlyList<HrTabs>? HrTabs = null)
{
    public static Me For(string id, string fullName, string email, UserRole role) =>
        new(id, fullName, email, role, RolePermissions.For(role).Order(StringComparer.Ordinal).ToList());
}
