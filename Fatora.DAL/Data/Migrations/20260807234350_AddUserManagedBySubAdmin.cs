using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserManagedBySubAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ManagedBySubAdminId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ManagedBySubAdminId",
                table: "Users",
                column: "ManagedBySubAdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_SubAdmins_ManagedBySubAdminId",
                table: "Users",
                column: "ManagedBySubAdminId",
                principalTable: "SubAdmins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_SubAdmins_ManagedBySubAdminId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ManagedBySubAdminId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ManagedBySubAdminId",
                table: "Users");
        }
    }
}
