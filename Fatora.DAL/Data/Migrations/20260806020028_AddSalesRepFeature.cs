using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesRepFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSalesManager",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByRepId",
                table: "Receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByRepId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByRepId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Reps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QrToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ProductAccessMode = table.Column<string>(type: "text", nullable: false),
                    CustomerAccessMode = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reps_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RepId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepRefreshTokens_Reps_RepId",
                        column: x => x.RepId,
                        principalTable: "Reps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CreatedByRepId",
                table: "Receipts",
                column: "CreatedByRepId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedByRepId",
                table: "Orders",
                column: "CreatedByRepId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedByRepId",
                table: "Customers",
                column: "CreatedByRepId");

            migrationBuilder.CreateIndex(
                name: "IX_RepRefreshTokens_RepId",
                table: "RepRefreshTokens",
                column: "RepId");

            migrationBuilder.CreateIndex(
                name: "IX_RepRefreshTokens_Token",
                table: "RepRefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reps_OwnerUserId",
                table: "Reps",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reps_QrToken",
                table: "Reps",
                column: "QrToken",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Reps_CreatedByRepId",
                table: "Customers",
                column: "CreatedByRepId",
                principalTable: "Reps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Reps_CreatedByRepId",
                table: "Orders",
                column: "CreatedByRepId",
                principalTable: "Reps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Receipts_Reps_CreatedByRepId",
                table: "Receipts",
                column: "CreatedByRepId",
                principalTable: "Reps",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Reps_CreatedByRepId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Reps_CreatedByRepId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Receipts_Reps_CreatedByRepId",
                table: "Receipts");

            migrationBuilder.DropTable(
                name: "RepRefreshTokens");

            migrationBuilder.DropTable(
                name: "Reps");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_CreatedByRepId",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedByRepId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CreatedByRepId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsSalesManager",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreatedByRepId",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "CreatedByRepId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreatedByRepId",
                table: "Customers");
        }
    }
}
