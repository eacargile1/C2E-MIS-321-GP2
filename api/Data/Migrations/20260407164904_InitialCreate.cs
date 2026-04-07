using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace C2E.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimesheetLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Client = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Project = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Task = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Hours = table.Column<decimal>(type: "numeric", nullable: false),
                    IsBillable = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimesheetLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetLines_UserId_WorkDate",
                table: "TimesheetLines",
                columns: new[] { "UserId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetLines_UserId_WorkDate_Client_Project_Task",
                table: "TimesheetLines",
                columns: new[] { "UserId", "WorkDate", "Client", "Project", "Task" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimesheetLines");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
