using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class OmschrijvingPerAdministratie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Omschrijving",
                table: "Relaties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Standaardomschrijving",
                table: "Administraties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StandaardomschrijvingVerkoop",
                table: "Administraties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Omschrijving",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Standaardomschrijving",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "StandaardomschrijvingVerkoop",
                table: "Administraties");
        }
    }
}
