using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncedAtToOrderReceiptPurchaseRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedAt",
                table: "Receipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedAt",
                table: "PurchaseRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SyncedAt",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "SyncedAt",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "SyncedAt",
                table: "Orders");
        }
    }
}
