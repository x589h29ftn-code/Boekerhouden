using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class IndexenBoekingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Verkoopdocumenten_BoekingId",
                table: "Verkoopdocumenten",
                column: "BoekingId");

            migrationBuilder.CreateIndex(
                name: "IX_Documenten_BoekingId",
                table: "Documenten",
                column: "BoekingId");

            migrationBuilder.CreateIndex(
                name: "IX_Boekingen_AdministratieId_Datum",
                table: "Boekingen",
                columns: new[] { "AdministratieId", "Datum" });

            migrationBuilder.CreateIndex(
                name: "IX_Banktransacties_BoekingId",
                table: "Banktransacties",
                column: "BoekingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Verkoopdocumenten_BoekingId",
                table: "Verkoopdocumenten");

            migrationBuilder.DropIndex(
                name: "IX_Documenten_BoekingId",
                table: "Documenten");

            migrationBuilder.DropIndex(
                name: "IX_Boekingen_AdministratieId_Datum",
                table: "Boekingen");

            migrationBuilder.DropIndex(
                name: "IX_Banktransacties_BoekingId",
                table: "Banktransacties");
        }
    }
}
