using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNextInvoiceNumberToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextInvoiceNumber",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Defaulting every user to 1 is only correct for users with no existing orders. Anyone
            // who already has invoices (e.g. INV-0001, INV-0002, ...) must resume counting from their
            // real highest number, or the very next invoice they create would collide with one they
            // already have.
            migrationBuilder.Sql(@"
                UPDATE ""Users"" u
                SET ""NextInvoiceNumber"" = COALESCE(
                    (SELECT MAX(CAST(SUBSTRING(o.""InvoiceNumber"" FROM 5) AS INTEGER)) + 1
                     FROM ""Orders"" o
                     WHERE o.""UserId"" = u.""Id""),
                    1
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextInvoiceNumber",
                table: "Users");
        }
    }
}
