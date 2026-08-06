using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRepRefreshTokenSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IssuedAtSessionVersion",
                table: "RepRefreshTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfills existing rows to their rep's *current* SessionVersion
            // rather than leaving the column default of 0 - without this,
            // every rep with an active session at deploy time would fail
            // TryRefreshAsync's new mismatch check on their very next
            // refresh (0 != whatever their real SessionVersion is), forcing
            // a mass, one-time logout of every currently-active rep purely
            // as a side effect of this migration rather than an actual
            // logout/deactivate action.
            migrationBuilder.Sql(
                "UPDATE \"RepRefreshTokens\" rt " +
                "SET \"IssuedAtSessionVersion\" = r.\"SessionVersion\" " +
                "FROM \"Reps\" r " +
                "WHERE r.\"Id\" = rt.\"RepId\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssuedAtSessionVersion",
                table: "RepRefreshTokens");
        }
    }
}
