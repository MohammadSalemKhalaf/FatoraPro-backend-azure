using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenPasswordResetOtp : Migration
    {
        /// <inheritdoc />
        // Code is dropped rather than migrated into CodeHash on purpose: it held
        // the plaintext recovery code, which is exactly what must stop being
        // stored, and every row is a single-use secret that expires 10 minutes
        // after it was issued. Any row still outstanding at deploy time simply
        // stops matching (an empty CodeHash can never equal a SHA-256 digest)
        // and is retired by the next forgot-password request.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PasswordResetOtps_UserId_Code",
                table: "PasswordResetOtps");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "PasswordResetOtps");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "PasswordResetOtps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CodeHash",
                table: "PasswordResetOtps",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetOtps_UserId_Used",
                table: "PasswordResetOtps",
                columns: new[] { "UserId", "Used" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PasswordResetOtps_UserId_Used",
                table: "PasswordResetOtps");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "PasswordResetOtps");

            migrationBuilder.DropColumn(
                name: "CodeHash",
                table: "PasswordResetOtps");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "PasswordResetOtps",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetOtps_UserId_Code",
                table: "PasswordResetOtps",
                columns: new[] { "UserId", "Code" });
        }
    }
}
