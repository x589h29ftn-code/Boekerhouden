using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Overzichtsweergave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OverzichtHeleEuros",
                table: "Kantoorinstellingen",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OverzichtLetter",
                table: "Kantoorinstellingen",
                type: "integer",
                nullable: false,
                defaultValue: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverzichtHeleEuros",
                table: "Kantoorinstellingen");

            migrationBuilder.DropColumn(
                name: "OverzichtLetter",
                table: "Kantoorinstellingen");
        }
    }
}
