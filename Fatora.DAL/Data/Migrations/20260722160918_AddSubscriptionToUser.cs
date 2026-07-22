using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Street",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionEnd",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            // Existing users keep working exactly as before: Lifetime (never expires), backfilled
            // to "now" as a start date since there's no real historical value to recover - matching
            // the same backfill approach already used for Product/Customer CreatedAt/UpdatedAt.
            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionStart",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionType",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "Lifetime");

            // The application always sets these explicitly on every insert (Trial on registration,
            // whatever the admin picks on a subscription change), so no lingering server-side
            // default should remain once existing rows are backfilled.
            migrationBuilder.Sql(@"ALTER TABLE ""Users"" ALTER COLUMN ""SubscriptionStart"" DROP DEFAULT;");
            migrationBuilder.Sql(@"ALTER TABLE ""Users"" ALTER COLUMN ""SubscriptionType"" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriptionEnd",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionStart",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionType",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Street",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
