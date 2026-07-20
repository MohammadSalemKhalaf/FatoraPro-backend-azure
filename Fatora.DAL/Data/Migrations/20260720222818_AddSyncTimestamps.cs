using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_UserId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Customers_UserId",
                table: "Customers");

            // Products/Customers never had any timestamp before - backfill existing rows to "now"
            // since there is no real historical value to recover.
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Products",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Products",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            // Orders already had a real CreatedAt. For existing rows, the best available approximation
            // of "last updated" is the moment the order was created - so backfill from that column
            // instead of "now", rather than manufacturing a fake update time.
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(@"UPDATE ""Orders"" SET ""UpdatedAt"" = ""CreatedAt"" WHERE ""UpdatedAt"" IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_UserId_UpdatedAt",
                table: "Products",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_UpdatedAt",
                table: "Orders",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_UserId_UpdatedAt",
                table: "Customers",
                columns: new[] { "UserId", "UpdatedAt" });

            // The application (via AppDbContext.SaveChanges) always stamps these explicitly on every
            // insert/update, so no lingering server-side default should remain.
            migrationBuilder.Sql(@"ALTER TABLE ""Products"" ALTER COLUMN ""CreatedAt"" DROP DEFAULT;");
            migrationBuilder.Sql(@"ALTER TABLE ""Products"" ALTER COLUMN ""UpdatedAt"" DROP DEFAULT;");
            migrationBuilder.Sql(@"ALTER TABLE ""Customers"" ALTER COLUMN ""CreatedAt"" DROP DEFAULT;");
            migrationBuilder.Sql(@"ALTER TABLE ""Customers"" ALTER COLUMN ""UpdatedAt"" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_UserId_UpdatedAt",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_UpdatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Customers_UserId_UpdatedAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Products_UserId",
                table: "Products",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_UserId",
                table: "Customers",
                column: "UserId");
        }
    }
}
