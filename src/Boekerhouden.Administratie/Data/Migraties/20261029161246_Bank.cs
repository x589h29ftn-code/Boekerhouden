using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
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
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Afschriften",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dagboek = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    Nummer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Begindatum = table.Column<DateOnly>(type: "date", nullable: true),
                    Beginsaldo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Einddatum = table.Column<DateOnly>(type: "date", nullable: true),
                    Eindsaldo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Bestandsnaam = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Melding = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IngelezenOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngelezenDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    AfschriftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    Dagboek = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Bedrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TegenIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    TegenNaam = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Betalingskenmerk = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EndToEndId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sleutel = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Melding = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BoekingId = table.Column<Guid>(type: "uuid", nullable: true)
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
