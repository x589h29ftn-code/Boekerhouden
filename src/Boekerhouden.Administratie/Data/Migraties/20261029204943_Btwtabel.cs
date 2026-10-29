using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Btwtabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Btwcodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Soort = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Tarief = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Actief = table.Column<bool>(type: "boolean", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Btwcodes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Btwtarieven",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tarief = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Vanaf = table.Column<DateOnly>(type: "date", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Btwtarieven", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Btwcodes",
                columns: new[] { "Code", "Actief", "Omschrijving", "Soort", "Tarief", "Volgorde" },
                values: new object[,]
                {
                    { "GB", true, "Geen btw", "GeenBtw", "Geen", 140 },
                    { "I0", true, "Inkoop 0% of vrijgesteld", "InkoopNul", "Nul", 100 },
                    { "I21", true, "Inkoop hoog tarief", "InkoopVoorbelasting", "Hoog", 80 },
                    { "I9", true, "Inkoop laag tarief", "InkoopVoorbelasting", "Laag", 90 },
                    { "IEU", true, "Inkoop binnen de EU", "InkoopBinnenEu", "Hoog", 130 },
                    { "IEX", true, "Inkoop buiten de EU", "InkoopBuitenEu", "Hoog", 120 },
                    { "IVL", true, "Inkoop, btw naar u verlegd", "InkoopVerlegd", "Hoog", 110 },
                    { "V0", true, "Verkoop 0%", "VerkoopNul", "Nul", 30 },
                    { "V21", true, "Verkoop hoog tarief", "VerkoopHoog", "Hoog", 10 },
                    { "V9", true, "Verkoop laag tarief", "VerkoopLaag", "Laag", 20 },
                    { "VEU", true, "Verkoop binnen de EU", "VerkoopBinnenEu", "Nul", 60 },
                    { "VEX", true, "Verkoop buiten de EU (uitvoer)", "VerkoopBuitenEu", "Nul", 50 },
                    { "VVL", true, "Verkoop, btw verlegd naar de afnemer", "VerkoopVerlegd", "Geen", 40 },
                    { "VVR", true, "Verkoop vrijgesteld", "VerkoopVrijgesteld", "Geen", 70 }
                });

            migrationBuilder.InsertData(
                table: "Btwtarieven",
                columns: new[] { "Id", "Percentage", "Tarief", "Vanaf" },
                values: new object[,]
                {
                    { 1, 19m, "Hoog", new DateOnly(2001, 1, 1) },
                    { 2, 21m, "Hoog", new DateOnly(2012, 10, 1) },
                    { 3, 6m, "Laag", new DateOnly(2001, 1, 1) },
                    { 4, 9m, "Laag", new DateOnly(2019, 1, 1) },
                    { 5, 0m, "Nul", new DateOnly(2001, 1, 1) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Btwtarieven_Tarief_Vanaf",
                table: "Btwtarieven",
                columns: new[] { "Tarief", "Vanaf" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Btwcodes");

            migrationBuilder.DropTable(
                name: "Btwtarieven");
        }
    }
}
