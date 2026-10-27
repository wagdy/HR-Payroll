using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanySettingsAndLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanySettings",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurrencySymbol = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CurrencyPosition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Country = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkdayStartsAtHour = table.Column<int>(type: "integer", nullable: false),
                    Weekends = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LogoFileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LogoContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LogoBytes = table.Column<int>(type: "integer", nullable: true),
                    LogoUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                    table.CheckConstraint("CK_CompanySettings_Single", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "CompanyLogos",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyLogos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyLogos_CompanySettings_Id",
                        column: x => x.Id,
                        principalSchema: "hr",
                        principalTable: "CompanySettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyLogos",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "CompanySettings",
                schema: "hr");
        }
    }
}
