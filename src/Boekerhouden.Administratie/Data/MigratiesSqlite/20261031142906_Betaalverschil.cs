using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Betaalverschil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Betaalverschil",
                table: "Kantoorinstellingen",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verschilrekening",
                table: "Kantoorinstellingen",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Betaalverschil",
                table: "Administraties",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verschilrekening",
                table: "Administraties",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Betaalverschil",
                table: "Kantoorinstellingen");

            migrationBuilder.DropColumn(
                name: "Verschilrekening",
                table: "Kantoorinstellingen");

            migrationBuilder.DropColumn(
                name: "Betaalverschil",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Verschilrekening",
                table: "Administraties");
        }
    }
}
