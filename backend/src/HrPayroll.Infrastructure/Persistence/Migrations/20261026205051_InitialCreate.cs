using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hr");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Module = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedById = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetCategories",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsConsumable = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceDevices",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastCalledInAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AttendanceStamp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCommands",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceSerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    QueuedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AnsweredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnCode = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceUsers",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceSerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceUserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultDaysPerYear = table.Column<int>(type: "integer", nullable: false),
                    ProratedForNewHires = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                    table.CheckConstraint("CK_LeaveTypes_Days", "\"DefaultDaysPerYear\" BETWEEN 0 AND 365");
                    table.CheckConstraint("CK_LeaveTypes_Unpaid", "\"IsPaid\" OR \"DefaultDaysPerYear\" = 0");
                });

            migrationBuilder.CreateTable(
                name: "PerformanceKpis",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceKpis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicHolidays",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FirstDay = table.Column<DateOnly>(type: "date", nullable: false),
                    LastDay = table.Column<DateOnly>(type: "date", nullable: false),
                    SetBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicHolidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    GraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    DoubleScanMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffHrAccess",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Tabs = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SetBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffHrAccess", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordSetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedSignInAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSignInAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobRoles",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobRoles_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "hr",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MobileNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IdPhotoFileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IdPhotoContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IdPhotoBytes = table.Column<int>(type: "integer", nullable: true),
                    IdPhotoUploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdPhotoUploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    JobTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    JobRoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmploymentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MonthlySalary = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    DailyWage = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftStartsAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    DaysOff = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceUserId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    UserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HiredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    LeftOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PinHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PinSetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedPinAttempts = table.Column<int>(type: "integer", nullable: false),
                    PinLockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HrTabs = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "hr",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_JobRoles_JobRoleId",
                        column: x => x.JobRoleId,
                        principalSchema: "hr",
                        principalTable: "JobRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetItems",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetItems", x => x.Id);
                    table.CheckConstraint("CK_AssetItems_Quantity", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_AssetItems_AssetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "hr",
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetItems_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceLogs",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeviceSerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceUserId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PunchedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeviceStatus = table.Column<int>(type: "integer", nullable: true),
                    VerifyMode = table.Column<int>(type: "integer", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IgnoredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IgnoredBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IgnoreReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LateSettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LateSettledBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceLogs_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyRecords",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstIn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastOut = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Punches = table.Column<int>(type: "integer", nullable: false),
                    WorkedMinutes = table.Column<int>(type: "integer", nullable: false),
                    LateMinutes = table.Column<int>(type: "integer", nullable: false),
                    EarlyLeaveMinutes = table.Column<int>(type: "integer", nullable: false),
                    AwayMinutes = table.Column<int>(type: "integer", nullable: false),
                    OvertimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    AdministrativePenaltyHours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    AdministrativePenaltyAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisciplinaryActions",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ViolationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SeverityLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PenaltyAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    IssuerId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    IssuerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsWithdrawn = table.Column<bool>(type: "boolean", nullable: false),
                    WithdrawnById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    WithdrawnBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WithdrawalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplinaryActions", x => x.Id);
                    table.CheckConstraint("CK_DisciplinaryActions_Penalty", "(\"SeverityLevel\" = 'PenaltyDeduction') = (\"PenaltyAmount\" IS NOT NULL) AND (\"PenaltyAmount\" IS NULL OR \"PenaltyAmount\" > 0)");
                    table.CheckConstraint("CK_DisciplinaryActions_Withdrawal", "\"IsWithdrawn\" = (\"WithdrawnById\" IS NOT NULL AND \"WithdrawnAtUtc\" IS NOT NULL AND \"WithdrawalReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DisciplinaryActions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeIdPhotos",
                schema: "hr",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeIdPhotos", x => x.EmployeeId);
                    table.ForeignKey(
                        name: "FK_EmployeeIdPhotos_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLeaveBalances",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    TotalAllocatedDays = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    UsedDays = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    AllocatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllocatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLeaveBalances", x => x.Id);
                    table.CheckConstraint("CK_EmployeeLeaveBalances_Days", "\"TotalAllocatedDays\" BETWEEN 0 AND 365 AND \"UsedDays\" BETWEEN 0 AND \"TotalAllocatedDays\"");
                    table.CheckConstraint("CK_EmployeeLeaveBalances_Year", "\"Year\" BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "FK_EmployeeLeaveBalances_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeLeaveBalances_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalSchema: "hr",
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HrAdvances",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayrollMonth = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrAdvances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HrAdvances_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalDaysRequested = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ManagerNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    CancelledDays = table.Column<int>(type: "integer", nullable: false),
                    CancelledById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CancelledBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.CheckConstraint("CK_LeaveRequests_Cancellation", "(\"CancelledFrom\" IS NULL) = (\"CancelledAtUtc\" IS NULL) AND (\"CancelledFrom\" IS NULL OR \"Status\" IN ('Approved', 'Cancelled')) AND (\"Status\" <> 'Cancelled' OR (\"CancelledFrom\" IS NOT NULL AND \"CancelledDays\" = \"TotalDaysRequested\")) AND \"CancelledDays\" BETWEEN 0 AND \"TotalDaysRequested\"");
                    table.CheckConstraint("CK_LeaveRequests_Dates", "\"EndDate\" >= \"StartDate\" AND date_part('year', \"EndDate\") = date_part('year', \"StartDate\") AND \"EndDate\" - \"StartDate\" < 90");
                    table.CheckConstraint("CK_LeaveRequests_Days", "\"TotalDaysRequested\" > 0 AND \"TotalDaysRequested\" <= \"EndDate\" - \"StartDate\" + 1");
                    table.CheckConstraint("CK_LeaveRequests_Decision", "(\"Status\" = 'Pending') = (\"DecidedById\" IS NULL AND \"DecidedAtUtc\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalSchema: "hr",
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollAdjustments",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ForDay = table.Column<DateOnly>(type: "date", nullable: true),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollSlips",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetPayable = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CarriedForward = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    WagesCost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalizedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Figures_AbsencePenalty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_AbsentDays = table.Column<int>(type: "integer", nullable: false),
                    Figures_AdministrativePenalty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_AdministrativePenaltyAmounts = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_AdministrativePenaltyHours = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_Advances = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_BaseSalary = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_BroughtForward = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_DailyWage = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_DaysEmployed = table.Column<int>(type: "integer", nullable: false),
                    Figures_DaysInMonth = table.Column<int>(type: "integer", nullable: false),
                    Figures_DaysOffWorked = table.Column<int>(type: "integer", nullable: false),
                    Figures_DaysOffWorkedPay = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_DaysWorked = table.Column<int>(type: "integer", nullable: false),
                    Figures_EmploymentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Figures_HolidaysWorked = table.Column<int>(type: "integer", nullable: false),
                    Figures_HolidaysWorkedPay = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_Incentives = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_LatenessPenalty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_LeaveAllowances = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_MissingPunchDays = table.Column<int>(type: "integer", nullable: false),
                    Figures_MonthlySalary = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_OpenDays = table.Column<int>(type: "integer", nullable: false),
                    Figures_OvertimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    Figures_OvertimePay = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Figures_ShortMinutes = table.Column<int>(type: "integer", nullable: false),
                    Figures_UnpaidLeaveDays = table.Column<int>(type: "integer", nullable: false),
                    Figures_UnpaidLeaveDeduction = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSlips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollSlips_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PerformanceEvaluations",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalScore = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewerId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ReviewerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceEvaluations", x => x.Id);
                    table.CheckConstraint("CK_PerformanceEvaluations_Total", "\"TotalScore\" BETWEEN 20 AND 100");
                    table.ForeignKey(
                        name: "FK_PerformanceEvaluations_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryPayments",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryPayments", x => x.Id);
                    table.CheckConstraint("CK_SalaryPayments_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_SalaryPayments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetTransactions",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTransactions", x => x.Id);
                    table.CheckConstraint("CK_AssetTransactions_Quantity", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_AssetTransactions_Reason", "(\"Type\" = 'CheckOut') = (\"Reason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AssetTransactions_AssetItems_AssetItemId",
                        column: x => x.AssetItemId,
                        principalSchema: "hr",
                        principalTable: "AssetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransactions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdministrativePenalties",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DisciplinaryActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SetBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministrativePenalties", x => x.Id);
                    table.CheckConstraint("CK_AdministrativePenalties_Disciplinary", "\"DisciplinaryActionId\" IS NULL OR \"Amount\" IS NOT NULL");
                    table.CheckConstraint("CK_AdministrativePenalties_HoursOrAmount", "(\"Hours\" > 0 AND \"Amount\" IS NULL) OR (\"Hours\" = 0 AND \"Amount\" > 0)");
                    table.ForeignKey(
                        name: "FK_AdministrativePenalties_DisciplinaryActions_DisciplinaryAct~",
                        column: x => x.DisciplinaryActionId,
                        principalSchema: "hr",
                        principalTable: "DisciplinaryActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdministrativePenalties_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdvanceAllocations",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PayrollMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AllocatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllocatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceAllocations_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceAllocations_HrAdvances_AdvanceId",
                        column: x => x.AdvanceId,
                        principalSchema: "hr",
                        principalTable: "HrAdvances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DayMarks",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MarkedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MarkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaveRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayMarks", x => x.Id);
                    table.CheckConstraint("CK_DayMarks_UnpaidLeave", "\"Kind\" <> 'UnpaidLeave' OR \"LeaveRequestId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_DayMarks_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DayMarks_LeaveRequests_LeaveRequestId",
                        column: x => x.LeaveRequestId,
                        principalSchema: "hr",
                        principalTable: "LeaveRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationScores",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KpiId = table.Column<Guid>(type: "uuid", nullable: false),
                    KpiName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationScores", x => x.Id);
                    table.CheckConstraint("CK_EvaluationScores_Score", "\"Score\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_EvaluationScores_PerformanceEvaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "hr",
                        principalTable: "PerformanceEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvaluationScores_PerformanceKpis_KpiId",
                        column: x => x.KpiId,
                        principalSchema: "hr",
                        principalTable: "PerformanceKpis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "hr",
                table: "Departments",
                columns: new[] { "Id", "CreatedAtUtc", "Name" },
                values: new object[] { new Guid("7a1e3c4d-0000-4000-8000-000000000001"), new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Utc), "General" });

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativePenalties_DisciplinaryActionId",
                schema: "hr",
                table: "AdministrativePenalties",
                column: "DisciplinaryActionId",
                unique: true,
                filter: "\"DisciplinaryActionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativePenalties_EmployeeId_Date",
                schema: "hr",
                table: "AdministrativePenalties",
                columns: new[] { "EmployeeId", "Date" },
                unique: true,
                filter: "\"DisciplinaryActionId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceAllocations_AdvanceId",
                schema: "hr",
                table: "AdvanceAllocations",
                column: "AdvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceAllocations_EmployeeId_PayrollMonth",
                schema: "hr",
                table: "AdvanceAllocations",
                columns: new[] { "EmployeeId", "PayrollMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_RecordId",
                schema: "hr",
                table: "ApprovalRequests",
                column: "RecordId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Status_RequestedAtUtc",
                schema: "hr",
                table: "ApprovalRequests",
                columns: new[] { "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_Name",
                schema: "hr",
                table: "AssetCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetItems_CategoryId",
                schema: "hr",
                table: "AssetItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetItems_EmployeeId",
                schema: "hr",
                table: "AssetItems",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_AssetItemId_Date",
                schema: "hr",
                table: "AssetTransactions",
                columns: new[] { "AssetItemId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_Date",
                schema: "hr",
                table: "AssetTransactions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransactions_EmployeeId",
                schema: "hr",
                table: "AssetTransactions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDevices_SerialNumber",
                schema: "hr",
                table: "AttendanceDevices",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_DeviceSerialNumber_DeviceUserId_PunchedAt",
                schema: "hr",
                table: "AttendanceLogs",
                columns: new[] { "DeviceSerialNumber", "DeviceUserId", "PunchedAt" },
                unique: true,
                filter: "\"DeviceSerialNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_DeviceUserId",
                schema: "hr",
                table: "AttendanceLogs",
                column: "DeviceUserId",
                filter: "\"EmployeeId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_EmployeeId_PunchedAt",
                schema: "hr",
                table: "AttendanceLogs",
                columns: new[] { "EmployeeId", "PunchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRecords_EmployeeId_Date",
                schema: "hr",
                table: "DailyRecords",
                columns: new[] { "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayMarks_EmployeeId_Date",
                schema: "hr",
                table: "DayMarks",
                columns: new[] { "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayMarks_LeaveRequestId",
                schema: "hr",
                table: "DayMarks",
                column: "LeaveRequestId",
                filter: "\"LeaveRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCommands_DeviceSerialNumber_Number",
                schema: "hr",
                table: "DeviceCommands",
                columns: new[] { "DeviceSerialNumber", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceUsers_DeviceSerialNumber_DeviceUserId",
                schema: "hr",
                table: "DeviceUsers",
                columns: new[] { "DeviceSerialNumber", "DeviceUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplinaryActions_EmployeeId_Date",
                schema: "hr",
                table: "DisciplinaryActions",
                columns: new[] { "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveBalances_EmployeeId_LeaveTypeId_Year",
                schema: "hr",
                table: "EmployeeLeaveBalances",
                columns: new[] { "EmployeeId", "LeaveTypeId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveBalances_LeaveTypeId",
                schema: "hr",
                table: "EmployeeLeaveBalances",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveBalances_Year",
                schema: "hr",
                table: "EmployeeLeaveBalances",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId",
                schema: "hr",
                table: "Employees",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DeviceUserId",
                schema: "hr",
                table: "Employees",
                column: "DeviceUserId",
                unique: true,
                filter: "\"DeviceUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_JobRoleId",
                schema: "hr",
                table: "Employees",
                column: "JobRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_MobileNumber",
                schema: "hr",
                table: "Employees",
                column: "MobileNumber",
                unique: true,
                filter: "\"MobileNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NationalId",
                schema: "hr",
                table: "Employees",
                column: "NationalId",
                unique: true,
                filter: "\"NationalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_UserAccountId",
                schema: "hr",
                table: "Employees",
                column: "UserAccountId",
                unique: true,
                filter: "\"UserAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_EvaluationId_KpiId",
                schema: "hr",
                table: "EvaluationScores",
                columns: new[] { "EvaluationId", "KpiId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_KpiId",
                schema: "hr",
                table: "EvaluationScores",
                column: "KpiId");

            migrationBuilder.CreateIndex(
                name: "IX_HrAdvances_EmployeeId_PayrollMonth",
                schema: "hr",
                table: "HrAdvances",
                columns: new[] { "EmployeeId", "PayrollMonth" },
                filter: "\"EmployeeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JobRoles_DepartmentId_Name",
                schema: "hr",
                table: "JobRoles",
                columns: new[] { "DepartmentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId_StartDate",
                schema: "hr",
                table: "LeaveRequests",
                columns: new[] { "EmployeeId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_LeaveTypeId",
                schema: "hr",
                table: "LeaveRequests",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_Status",
                schema: "hr",
                table: "LeaveRequests",
                column: "Status",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_Name",
                schema: "hr",
                table: "LeaveTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_EmployeeId_Month",
                schema: "hr",
                table: "PayrollAdjustments",
                columns: new[] { "EmployeeId", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSlips_EmployeeId_Month",
                schema: "hr",
                table: "PayrollSlips",
                columns: new[] { "EmployeeId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSlips_Month",
                schema: "hr",
                table: "PayrollSlips",
                column: "Month");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceEvaluations_EmployeeId_Date",
                schema: "hr",
                table: "PerformanceEvaluations",
                columns: new[] { "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceKpis_Name",
                schema: "hr",
                table: "PerformanceKpis",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicHolidays_FirstDay_LastDay",
                schema: "hr",
                table: "PublicHolidays",
                columns: new[] { "FirstDay", "LastDay" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryPayments_EmployeeId_PayrollMonth",
                schema: "hr",
                table: "SalaryPayments",
                columns: new[] { "EmployeeId", "PayrollMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryPayments_PayrollMonth",
                schema: "hr",
                table: "SalaryPayments",
                column: "PayrollMonth");

            migrationBuilder.CreateIndex(
                name: "IX_StaffHrAccess_StaffId",
                schema: "hr",
                table: "StaffHrAccess",
                column: "StaffId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                schema: "identity",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            // The kinds of leave the system starts with: Annual 21 days (a new hire's share of
            // the year), Sick 7, and Unpaid.
            migrationBuilder.InsertData(
                schema: "hr",
                table: "LeaveTypes",
                columns: ["Id", "Name", "IsPaid", "DefaultDaysPerYear", "ProratedForNewHires", "CreatedAtUtc"],
                values: new object[,]
                {
                    { new Guid("6d0c5e1a-3f0b-4b5e-9a11-0a5e1a7e0001"), "Annual (اعتيادي)", true, 21, true, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("6d0c5e1a-3f0b-4b5e-9a11-0a5e1a7e0002"), "Sick (مرضي)", true, 7, false, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("6d0c5e1a-3f0b-4b5e-9a11-0a5e1a7e0003"), "Unpaid (بدون راتب)", false, 0, false, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc) },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministrativePenalties",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AdvanceAllocations",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "ApprovalRequests",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AssetTransactions",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AttendanceDevices",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AttendanceLogs",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "DailyRecords",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "DayMarks",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "DeviceCommands",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "DeviceUsers",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "EmployeeIdPhotos",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "EmployeeLeaveBalances",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "EvaluationScores",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "PayrollAdjustments",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "PayrollSlips",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "PublicHolidays",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "SalaryPayments",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "StaffHrAccess",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "DisciplinaryActions",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "HrAdvances",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AssetItems",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "LeaveRequests",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "PerformanceEvaluations",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "PerformanceKpis",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AssetCategories",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "LeaveTypes",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "Employees",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "JobRoles",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "Departments",
                schema: "hr");
        }
    }
}
