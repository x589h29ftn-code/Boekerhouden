using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class VasteActiva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Activa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nummer = table.Column<int>(type: "INTEGER", nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Aanschafdatum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Aanschafwaarde = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Restwaarde = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    BeginAfschrijving = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Looptijd = table.Column<int>(type: "INTEGER", nullable: false),
                    Methode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Activarekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Afschrijvingsrekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Kostenrekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AfgeschrevenVoorBegin = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Bron = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Notitie = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    BuitenGebruikOp = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Opbrengst = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: true),
                    Desinvesteringsboeking = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AangemaaktOp = table.Column<long>(type: "INTEGER", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Activa_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Afschrijvingen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivumId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Maand = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Bedrag = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Boekstuk = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Afschrijvingen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Afschrijvingen_Activa_ActivumId",
                        column: x => x.ActivumId,
                        principalTable: "Activa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Afschrijvingen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activa_AdministratieId_Nummer",
                table: "Activa",
                columns: new[] { "AdministratieId", "Nummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Afschrijvingen_ActivumId_Maand",
                table: "Afschrijvingen",
                columns: new[] { "ActivumId", "Maand" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Afschrijvingen_AdministratieId_Boekstuk",
                table: "Afschrijvingen",
                columns: new[] { "AdministratieId", "Boekstuk" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Afschrijvingen");

            migrationBuilder.DropTable(
                name: "Activa");
        }
    }
}
