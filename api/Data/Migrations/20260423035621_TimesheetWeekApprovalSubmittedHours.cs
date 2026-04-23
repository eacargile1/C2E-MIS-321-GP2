using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace C2E.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TimesheetWeekApprovalSubmittedHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SubmittedBillableHours",
                table: "TimesheetWeekApprovals",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SubmittedTotalHours",
                table: "TimesheetWeekApprovals",
                type: "decimal(18,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubmittedBillableHours",
                table: "TimesheetWeekApprovals");

            migrationBuilder.DropColumn(
                name: "SubmittedTotalHours",
                table: "TimesheetWeekApprovals");
        }
    }
}
