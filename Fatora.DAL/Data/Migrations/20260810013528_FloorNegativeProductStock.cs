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
            // One-time cleanup that zeroed stock which had gone negative.
            //
            // SUPERSEDED: the floor this went with has since been removed
            // from OrderService/SyncService's AdjustStock - negative stock
            // is now the correct, intended result of selling more than is
            // on hand, and flooring it desynced the server from the clients,
            // which never floored their own copy. Kept only because it has
            // already run; nothing should reintroduce it.
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
