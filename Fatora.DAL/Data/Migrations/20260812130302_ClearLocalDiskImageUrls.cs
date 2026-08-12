using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClearLocalDiskImageUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Image storage moved from the container's local disk (wiped by
            // Render's ephemeral filesystem on every redeploy/restart) to
            // Cloudinary. Any row still holding an old "/uploads/..." path
            // points at a file that is already gone and will never come
            // back - clearing it lets the app fall back to its normal
            // no-image placeholder instead of a permanently-broken image.
            migrationBuilder.Sql(
                "UPDATE \"Products\" SET \"ImageUrl\" = NULL WHERE \"ImageUrl\" LIKE '/uploads/%';");
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"LogoUrl\" = NULL WHERE \"LogoUrl\" LIKE '/uploads/%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible by design - the original local-disk paths aren't
            // meaningful to restore, since those files no longer exist.
        }
    }
}
