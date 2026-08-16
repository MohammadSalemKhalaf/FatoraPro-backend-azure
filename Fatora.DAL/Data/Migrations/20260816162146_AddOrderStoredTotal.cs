using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStoredTotal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Total",
                table: "Orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Backfill every existing row using the OLD formula (rounded to
            // the nearest half-unit, MidpointRounding.AwayFromZero - Postgres
            // numeric ROUND() rounds half away from zero too, matching it
            // exactly), computed from that row's own OrderItems/Discount/
            // CashDiscount. This freezes each already-issued invoice's Total
            // at exactly what it displayed before this migration; only a
            // future create/edit (OrderService/SyncService, now assigning
            // Total via the new unrounded ComputeTotal formula) changes it
            // from here on.
            migrationBuilder.Sql(@"
                UPDATE ""Orders"" o
                SET ""Total"" = ROUND(
                    (
                        COALESCE((SELECT SUM(oi.""UnitPrice"" * oi.""Quantity"") FROM ""OrderItems"" oi WHERE oi.""OrderId"" = o.""Id""), 0)
                        * (1 - o.""Discount"" / 100)
                        - o.""CashDiscount""
                    ) * 2, 0
                ) / 2;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Total",
                table: "Orders");
        }
    }
}
