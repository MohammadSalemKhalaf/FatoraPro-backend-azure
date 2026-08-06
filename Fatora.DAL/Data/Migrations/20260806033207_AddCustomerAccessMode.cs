using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fatora.DAL.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAccessMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing Rep's CustomerAccessMode is currently the
            // string "All" (R2's leftover, never-enforced default), but
            // every existing Rep's REAL behavior up to this point has
            // always been "sees only its own customers" - GetAllAsync
            // unconditionally filtered by CreatedByRepId with no mode check
            // at all. Now that this column is actually enforced, correct
            // every existing row to "Own" so real behavior doesn't silently
            // change the moment this migration runs - an owner has to
            // explicitly opt a Rep into All/Restricted from here on.
            migrationBuilder.Sql("UPDATE \"Reps\" SET \"CustomerAccessMode\" = 'Own' WHERE \"CustomerAccessMode\" = 'All';");

            migrationBuilder.CreateTable(
                name: "RepCustomerAccesses",
                columns: table => new
                {
                    RepId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepCustomerAccesses", x => new { x.RepId, x.CustomerId });
                    table.ForeignKey(
                        name: "FK_RepCustomerAccesses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepCustomerAccesses_Reps_RepId",
                        column: x => x.RepId,
                        principalTable: "Reps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepCustomerAccesses_CustomerId",
                table: "RepCustomerAccesses",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepCustomerAccesses");

            migrationBuilder.Sql("UPDATE \"Reps\" SET \"CustomerAccessMode\" = 'All' WHERE \"CustomerAccessMode\" = 'Own';");
        }
    }
}
