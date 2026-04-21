using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace C2E.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDeliveryManagerEngagementPartner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryManagerUserId",
                table: "Projects",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "EngagementPartnerUserId",
                table: "Projects",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_DeliveryManagerUserId",
                table: "Projects",
                column: "DeliveryManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_EngagementPartnerUserId",
                table: "Projects",
                column: "EngagementPartnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_DeliveryManagerUserId",
                table: "Projects",
                column: "DeliveryManagerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_EngagementPartnerUserId",
                table: "Projects",
                column: "EngagementPartnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Users_DeliveryManagerUserId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Users_EngagementPartnerUserId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_DeliveryManagerUserId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_EngagementPartnerUserId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DeliveryManagerUserId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "EngagementPartnerUserId",
                table: "Projects");
        }
    }
}
