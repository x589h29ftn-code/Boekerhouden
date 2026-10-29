using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Bank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "Dagboeken",
                type: "TEXT",
                maxLength: 34,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Afschriften",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dagboek = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                    Nummer = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Begindatum = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Beginsaldo = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Einddatum = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Eindsaldo = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Bestandsnaam = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Melding = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IngelezenOp = table.Column<long>(type: "INTEGER", nullable: false),
                    IngelezenDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Afschriften", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Afschriften_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Banktransacties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AfschriftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Volgorde = table.Column<int>(type: "INTEGER", nullable: false),
                    Dagboek = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                    Datum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Bedrag = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    TegenIban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true),
                    TegenNaam = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Betalingskenmerk = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    EndToEndId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Sleutel = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Melding = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    BoekingId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banktransacties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Banktransacties_Afschriften_AfschriftId",
                        column: x => x.AfschriftId,
                        principalTable: "Afschriften",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Afschriften_AdministratieId_Iban_Einddatum",
                table: "Afschriften",
                columns: new[] { "AdministratieId", "Iban", "Einddatum" });

            migrationBuilder.CreateIndex(
                name: "IX_Banktransacties_AdministratieId_Sleutel",
                table: "Banktransacties",
                columns: new[] { "AdministratieId", "Sleutel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Banktransacties_AdministratieId_Status",
                table: "Banktransacties",
                columns: new[] { "AdministratieId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Banktransacties_AfschriftId",
                table: "Banktransacties",
                column: "AfschriftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Banktransacties");

            migrationBuilder.DropTable(
                name: "Afschriften");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "Dagboeken");
        }
    }
}
