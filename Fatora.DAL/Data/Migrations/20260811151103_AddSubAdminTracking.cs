using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubAdminTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "SubscriptionActivations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "SubscriptionActivations",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubAdminAccountActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "text", nullable: false),
                    PerformedBySubAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubAdminAccountActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubAdminAccountActions_SubAdmins_PerformedBySubAdminId",
                        column: x => x.PerformedBySubAdminId,
                        principalTable: "SubAdmins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubAdminAccountActions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubAdminAccountActions_PerformedBySubAdminId_CreatedAt",
                table: "SubAdminAccountActions",
                columns: new[] { "PerformedBySubAdminId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubAdminAccountActions_UserId",
                table: "SubAdminAccountActions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubAdminAccountActions");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "SubscriptionActivations");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "SubscriptionActivations");
        }
    }
}
