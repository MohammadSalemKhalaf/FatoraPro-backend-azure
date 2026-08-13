using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenRotationGrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedAtUtc",
                table: "SubAdminRefreshTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedAtUtc",
                table: "RepRefreshTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedAtUtc",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplacedAtUtc",
                table: "SubAdminRefreshTokens");

            migrationBuilder.DropColumn(
                name: "ReplacedAtUtc",
                table: "RepRefreshTokens");

            migrationBuilder.DropColumn(
                name: "ReplacedAtUtc",
                table: "RefreshTokens");
        }
    }
}
