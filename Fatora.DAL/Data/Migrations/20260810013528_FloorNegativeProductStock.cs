using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class FloorNegativeProductStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-time cleanup: stock could go negative before OrderService/
            // SyncService's AdjustStock started flooring at 0. Going
            // forward nothing can produce a negative value again, so this
            // never needs to run more than once.
            migrationBuilder.Sql(
                "UPDATE \"Products\" SET \"StockQuantity\" = 0 WHERE \"StockQuantity\" < 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible by design - the original negative values aren't
            // recoverable, and reintroducing them would defeat the point.
        }
    }
}
