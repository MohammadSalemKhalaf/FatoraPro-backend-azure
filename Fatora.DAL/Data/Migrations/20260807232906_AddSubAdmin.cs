using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubAdmins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QrToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    SessionVersion = table.Column<int>(type: "integer", nullable: false),
                    CanActivateMonthly = table.Column<bool>(type: "boolean", nullable: false),
                    CanActivateAnnual = table.Column<bool>(type: "boolean", nullable: false),
                    CanActivateCustomMonths = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubAdmins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubAdminRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubAdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAtSessionVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubAdminRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubAdminRefreshTokens_SubAdmins_SubAdminId",
                        column: x => x.SubAdminId,
                        principalTable: "SubAdmins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubAdminRefreshTokens_SubAdminId",
                table: "SubAdminRefreshTokens",
                column: "SubAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_SubAdminRefreshTokens_Token",
                table: "SubAdminRefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubAdmins_QrToken",
                table: "SubAdmins",
                column: "QrToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubAdminRefreshTokens");

            migrationBuilder.DropTable(
                name: "SubAdmins");
        }
    }
}
