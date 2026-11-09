using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Privegebruik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Cataloguswaarde",
                table: "Activa",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Privegebruik",
                table: "Activa",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Onbekend");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cataloguswaarde",
                table: "Activa");

            migrationBuilder.DropColumn(
                name: "Privegebruik",
                table: "Activa");
        }
    }
}
