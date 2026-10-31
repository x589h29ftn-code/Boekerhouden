using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Betaalverschil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Betaalverschil",
                table: "Kantoorinstellingen",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verschilrekening",
                table: "Kantoorinstellingen",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Betaalverschil",
                table: "Administraties",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verschilrekening",
                table: "Administraties",
                type: "text",
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
