using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace C2E.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserManagerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ManagerUserId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ManagerUserId",
                table: "Users",
                column: "ManagerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_ManagerUserId",
                table: "Users",
                column: "ManagerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_ManagerUserId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ManagerUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ManagerUserId",
                table: "Users");
        }
    }
}
