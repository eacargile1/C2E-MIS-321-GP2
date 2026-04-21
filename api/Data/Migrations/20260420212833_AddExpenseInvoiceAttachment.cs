using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace C2E.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseInvoiceAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "InvoiceBytes",
                table: "ExpenseEntries",
                type: "longblob",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceContentType",
                table: "ExpenseEntries",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceFileName",
                table: "ExpenseEntries",
                type: "varchar(260)",
                maxLength: 260,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceBytes",
                table: "ExpenseEntries");

            migrationBuilder.DropColumn(
                name: "InvoiceContentType",
                table: "ExpenseEntries");

            migrationBuilder.DropColumn(
                name: "InvoiceFileName",
                table: "ExpenseEntries");
        }
    }
}
