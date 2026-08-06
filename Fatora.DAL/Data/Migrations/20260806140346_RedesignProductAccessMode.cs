using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class RedesignProductAccessMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Restricted" is renamed to "Selected" - the stored string
            // values need the same rename even though the column itself
            // (varchar, via HasConversion<string>()) never changes shape.
            migrationBuilder.Sql(
                "UPDATE \"Reps\" SET \"ProductAccessMode\" = 'Selected' WHERE \"ProductAccessMode\" = 'Restricted';");

            // Under the old model, a Restricted rep could see (and create)
            // its own products with no explicit RepProductAccess grant -
            // Selected mode no longer has that fallback (see
            // ProductService.ApplyRepScopeAsync), so this backfills an
            // explicit grant for anything such a rep had already created,
            // preserving access instead of silently taking it away on
            // upgrade.
            migrationBuilder.Sql(@"
                INSERT INTO ""RepProductAccesses"" (""RepId"", ""ProductId"")
                SELECT p.""CreatedByRepId"", p.""Id""
                FROM ""Products"" p
                JOIN ""Reps"" r ON r.""Id"" = p.""CreatedByRepId""
                WHERE p.""CreatedByRepId"" IS NOT NULL
                  AND r.""ProductAccessMode"" = 'Selected'
                  AND NOT EXISTS (
                      SELECT 1 FROM ""RepProductAccesses"" rpa
                      WHERE rpa.""RepId"" = p.""CreatedByRepId"" AND rpa.""ProductId"" = p.""Id""
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"Reps\" SET \"ProductAccessMode\" = 'Restricted' WHERE \"ProductAccessMode\" = 'Selected';");
            // The RepProductAccess rows backfilled in Up() are deliberately
            // left in place - they were harmless under the old model too (a
            // Restricted rep already implicitly saw its own creations
            // regardless), and there's no reliable way to tell them apart
            // from grants the owner made deliberately through the picker.
        }
    }
}
