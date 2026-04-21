using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace C2E.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPtoRequestSecondaryApprover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SecondaryApproverUserId",
                table: "PtoRequests",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_PtoRequests_SecondaryApproverUserId",
                table: "PtoRequests",
                column: "SecondaryApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PtoRequests_Status_SecondaryApproverUserId",
                table: "PtoRequests",
                columns: new[] { "Status", "SecondaryApproverUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PtoRequests_Users_SecondaryApproverUserId",
                table: "PtoRequests",
                column: "SecondaryApproverUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PtoRequests_Users_SecondaryApproverUserId",
                table: "PtoRequests");

            migrationBuilder.DropIndex(
                name: "IX_PtoRequests_SecondaryApproverUserId",
                table: "PtoRequests");

            migrationBuilder.DropIndex(
                name: "IX_PtoRequests_Status_SecondaryApproverUserId",
                table: "PtoRequests");

            migrationBuilder.DropColumn(
                name: "SecondaryApproverUserId",
                table: "PtoRequests");
        }
    }
}
