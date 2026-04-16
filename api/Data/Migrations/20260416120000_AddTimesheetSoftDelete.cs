using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace C2E.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheetSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "TimesheetLines",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TimesheetLines",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.DropIndex(
                name: "IX_TimesheetLines_UserId_WorkDate_Client_Project_Task",
                table: "TimesheetLines");

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetLines_UserId_WorkDate_Client_Project_Task_IsDeleted",
                table: "TimesheetLines",
                columns: new[] { "UserId", "WorkDate", "Client", "Project", "Task", "IsDeleted" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimesheetLines_UserId_WorkDate_Client_Project_Task_IsDeleted",
                table: "TimesheetLines");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "TimesheetLines");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TimesheetLines");

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetLines_UserId_WorkDate_Client_Project_Task",
                table: "TimesheetLines",
                columns: new[] { "UserId", "WorkDate", "Client", "Project", "Task" },
                unique: true);
        }
    }
}
