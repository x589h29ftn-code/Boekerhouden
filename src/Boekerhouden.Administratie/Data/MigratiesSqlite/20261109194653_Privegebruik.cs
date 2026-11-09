using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Privegebruik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Cataloguswaarde",
                table: "Activa",
                type: "INTEGER",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Privegebruik",
                table: "Activa",
                type: "TEXT",
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
