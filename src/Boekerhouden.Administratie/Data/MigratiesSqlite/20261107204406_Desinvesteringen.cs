using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Desinvesteringen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Desinvesteringen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivumId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Datum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Soort = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Deel = table.Column<long>(type: "INTEGER", precision: 5, scale: 2, nullable: false),
                    Aanschaf = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Afschrijving = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Restwaarde = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Opbrengst = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Opbrengstrekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Resultaatrekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Boekstuk = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Tegenboeking = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    GeboektOp = table.Column<long>(type: "INTEGER", nullable: false),
                    GeboektDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Desinvesteringen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Desinvesteringen_Activa_ActivumId",
                        column: x => x.ActivumId,
                        principalTable: "Activa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Desinvesteringen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Desinvesteringen_ActivumId",
                table: "Desinvesteringen",
                column: "ActivumId");

            migrationBuilder.CreateIndex(
                name: "IX_Desinvesteringen_AdministratieId_Boekstuk",
                table: "Desinvesteringen",
                columns: new[] { "AdministratieId", "Boekstuk" });

            // Wat eerder "buiten gebruik" was (het hele actief, zonder resultaatrekening), als desinvestering bewaren.
            migrationBuilder.Sql("""
                INSERT INTO "Desinvesteringen" ("AdministratieId", "ActivumId", "Datum", "Soort", "Deel", "Aanschaf", "Afschrijving", "Restwaarde",
                    "Opbrengst", "Opbrengstrekening", "Resultaatrekening", "Boekstuk", "GeboektOp", "GeboektDoor")
                SELECT a."AdministratieId", a."Id", a."BuitenGebruikOp", CASE WHEN COALESCE(a."Opbrengst", 0) = 0 THEN 'Sloop' ELSE 'Verkoop' END, 100,
                    a."Aanschafwaarde", a."AfgeschrevenVoorBegin" + COALESCE((SELECT SUM(x."Bedrag") FROM "Afschrijvingen" x WHERE x."ActivumId" = a."Id"), 0),
                    a."Restwaarde", COALESCE(a."Opbrengst", 0), NULL, '', a."Desinvesteringsboeking", a."AangemaaktOp", a."AangemaaktDoor"
                FROM "Activa" a WHERE a."BuitenGebruikOp" IS NOT NULL AND a."Desinvesteringsboeking" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "Desinvesteringsboeking",
                table: "Activa");

            migrationBuilder.DropColumn(
                name: "Opbrengst",
                table: "Activa");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Desinvesteringen");

            migrationBuilder.AddColumn<string>(
                name: "Desinvesteringsboeking",
                table: "Activa",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Opbrengst",
                table: "Activa",
                type: "INTEGER",
                precision: 18,
                scale: 2,
                nullable: true);
        }
    }
}
