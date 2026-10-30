using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Boekingsversies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Boekingen_AdministratieId_Dagboek_Boekstuk",
                table: "Boekingen");

            migrationBuilder.AddColumn<int>(
                name: "Versie",
                table: "Boekingen",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Boekingen_AdministratieId_Dagboek_Boekstuk_Versie",
                table: "Boekingen",
                columns: new[] { "AdministratieId", "Dagboek", "Boekstuk", "Versie" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Boekingen_AdministratieId_Dagboek_Boekstuk_Versie",
                table: "Boekingen");

            migrationBuilder.DropColumn(
                name: "Versie",
                table: "Boekingen");

            migrationBuilder.CreateIndex(
                name: "IX_Boekingen_AdministratieId_Dagboek_Boekstuk",
                table: "Boekingen",
                columns: new[] { "AdministratieId", "Dagboek", "Boekstuk" },
                unique: true);
        }
    }
}
