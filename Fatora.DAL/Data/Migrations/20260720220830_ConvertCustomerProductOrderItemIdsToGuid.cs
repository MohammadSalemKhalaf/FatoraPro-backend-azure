using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertCustomerProductOrderItemIdsToGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Postgres cannot cast integer -> uuid directly, and there is no meaningful way to derive
            // a uuid from an existing int id. So instead of ALTER COLUMN ... TYPE uuid, this migration:
            //   1. adds new uuid columns and fills them with freshly generated ids
            //   2. remaps every foreign key to point at the new uuid ids (via a join on the old int ids)
            //   3. drops the old int columns/constraints and renames the new columns into their place
            // This preserves every existing row and every existing relationship.

            migrationBuilder.AddColumn<Guid>(
                name: "NewId",
                table: "Customers",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "NewId",
                table: "Products",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "NewId",
                table: "OrderItems",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "NewCustomerId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NewProductId",
                table: "OrderItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"UPDATE ""Orders"" o SET ""NewCustomerId"" = c.""NewId"" FROM ""Customers"" c WHERE o.""CustomerId"" = c.""Id"";");
            migrationBuilder.Sql(@"UPDATE ""OrderItems"" oi SET ""NewProductId"" = p.""NewId"" FROM ""Products"" p WHERE oi.""ProductId"" = p.""Id"";");

            migrationBuilder.AlterColumn<Guid>(
                name: "NewCustomerId",
                table: "Orders",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "NewProductId",
                table: "OrderItems",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropForeignKey(name: "FK_Orders_Customers_CustomerId", table: "Orders");
            migrationBuilder.DropForeignKey(name: "FK_OrderItems_Products_ProductId", table: "OrderItems");

            migrationBuilder.DropPrimaryKey(name: "PK_Customers", table: "Customers");
            migrationBuilder.DropPrimaryKey(name: "PK_Products", table: "Products");
            migrationBuilder.DropPrimaryKey(name: "PK_OrderItems", table: "OrderItems");

            migrationBuilder.DropColumn(name: "Id", table: "Customers");
            migrationBuilder.DropColumn(name: "Id", table: "Products");
            migrationBuilder.DropColumn(name: "Id", table: "OrderItems");
            migrationBuilder.DropColumn(name: "CustomerId", table: "Orders");
            migrationBuilder.DropColumn(name: "ProductId", table: "OrderItems");

            migrationBuilder.RenameColumn(name: "NewId", table: "Customers", newName: "Id");
            migrationBuilder.RenameColumn(name: "NewId", table: "Products", newName: "Id");
            migrationBuilder.RenameColumn(name: "NewId", table: "OrderItems", newName: "Id");
            migrationBuilder.RenameColumn(name: "NewCustomerId", table: "Orders", newName: "CustomerId");
            migrationBuilder.RenameColumn(name: "NewProductId", table: "OrderItems", newName: "ProductId");

            migrationBuilder.AddPrimaryKey(name: "PK_Customers", table: "Customers", column: "Id");
            migrationBuilder.AddPrimaryKey(name: "PK_Products", table: "Products", column: "Id");
            migrationBuilder.AddPrimaryKey(name: "PK_OrderItems", table: "OrderItems", column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // The application (matching Order.Id's existing pattern) generates new ids client-side via
            // Guid.NewGuid() at insert time, so no server-side default should linger on these columns.
            migrationBuilder.Sql(@"ALTER TABLE ""Customers"" ALTER COLUMN ""Id"" DROP DEFAULT;");
            migrationBuilder.Sql(@"ALTER TABLE ""Products"" ALTER COLUMN ""Id"" DROP DEFAULT;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ALTER COLUMN ""Id"" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Reverting the Customer/Product/OrderItem Guid conversion is not supported: the original " +
                "sequential int ids are not recoverable once dropped. Restore from a backup taken before " +
                "this migration was applied instead.");
        }
    }
}
