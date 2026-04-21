using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace C2E.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAssignedFinanceUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedFinanceUserId",
                table: "Projects",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_AssignedFinanceUserId",
                table: "Projects",
                column: "AssignedFinanceUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_AssignedFinanceUserId",
                table: "Projects",
                column: "AssignedFinanceUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Users_AssignedFinanceUserId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_AssignedFinanceUserId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "AssignedFinanceUserId",
                table: "Projects");
        }
    }
}
