using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Leren : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Omschrijving",
                table: "Regels",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Bedrag",
                table: "Leerkeuzes",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "Leerkeuzes",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Omschrijving",
                table: "Leerkeuzes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Postcodes",
                table: "Leerkeuzes",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DrempelAutomatisch",
                table: "Administraties",
                type: "integer",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Omschrijving",
                table: "Regels");

            migrationBuilder.DropColumn(
                name: "Bedrag",
                table: "Leerkeuzes");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "Leerkeuzes");

            migrationBuilder.DropColumn(
                name: "Omschrijving",
                table: "Leerkeuzes");

            migrationBuilder.DropColumn(
                name: "Postcodes",
                table: "Leerkeuzes");

            migrationBuilder.DropColumn(
                name: "DrempelAutomatisch",
                table: "Administraties");
        }
    }
}
