using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCreatedByRepId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByRepId",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CreatedByRepId",
                table: "Products",
                column: "CreatedByRepId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Reps_CreatedByRepId",
                table: "Products",
                column: "CreatedByRepId",
                principalTable: "Reps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Reps_CreatedByRepId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_CreatedByRepId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CreatedByRepId",
                table: "Products");
        }
    }
}
