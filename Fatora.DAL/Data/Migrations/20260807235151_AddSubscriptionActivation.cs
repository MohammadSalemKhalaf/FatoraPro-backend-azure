using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionActivations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionType = table.Column<string>(type: "text", nullable: false),
                    CustomMonths = table.Column<int>(type: "integer", nullable: true),
                    PerformedBySubAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionActivations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionActivations_SubAdmins_PerformedBySubAdminId",
                        column: x => x.PerformedBySubAdminId,
                        principalTable: "SubAdmins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionActivations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionActivations_PerformedBySubAdminId_ActivatedAt",
                table: "SubscriptionActivations",
                columns: new[] { "PerformedBySubAdminId", "ActivatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionActivations_UserId",
                table: "SubscriptionActivations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionActivations");
        }
    }
}
