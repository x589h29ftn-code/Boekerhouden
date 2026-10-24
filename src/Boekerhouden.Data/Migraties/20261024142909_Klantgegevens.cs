using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Klantgegevens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Klanten",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Huisnummer",
                table: "Klanten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Oprichtingsdatum",
                table: "Klanten",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                table: "Klanten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rsin",
                table: "Klanten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SbiCode",
                table: "Klanten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatutaireNaam",
                table: "Klanten",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatutaireZetel",
                table: "Klanten",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Straat",
                table: "Klanten",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefoon",
                table: "Klanten",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Toevoeging",
                table: "Klanten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Klanten",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "Huisnummer",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "Oprichtingsdatum",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "Postcode",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "Rsin",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "SbiCode",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "StatutaireNaam",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "StatutaireZetel",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "Straat",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "Telefoon",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "Toevoeging",
                table: "Klanten");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Klanten");
        }
    }
}
