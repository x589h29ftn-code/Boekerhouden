using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Verkoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Betalingstermijn",
                table: "Relaties",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Contactpersoon",
                table: "Relaties",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Relaties",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Klant",
                table: "Relaties",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Land",
                table: "Relaties",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Omzetrekening",
                table: "Relaties",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plaats",
                table: "Relaties",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                table: "Relaties",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Straat",
                table: "Relaties",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verkoopbtwcode",
                table: "Relaties",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Administraties",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Land",
                table: "Administraties",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plaats",
                table: "Administraties",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                table: "Administraties",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Straat",
                table: "Administraties",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefoon",
                table: "Administraties",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Administraties",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Verkoopdocumenten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Nummer = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Relatie = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Klantgegevens = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Datum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Vervaldatum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Referentie = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Tekst = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Betalingskenmerk = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreditVoorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BoekingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Verdeling = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    LayoutId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AbonnementId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AangemaaktOp = table.Column<long>(type: "INTEGER", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefinitiefOp = table.Column<long>(type: "INTEGER", nullable: true),
                    DefinitiefDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verkoopdocumenten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Verkoopdocumenten_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Verkoopdocumentregels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VerkoopdocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Volgorde = table.Column<int>(type: "INTEGER", nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Aantal = table.Column<long>(type: "INTEGER", precision: 18, scale: 4, nullable: false),
                    Eenheid = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Prijs = table.Column<long>(type: "INTEGER", precision: 18, scale: 4, nullable: false),
                    Korting = table.Column<long>(type: "INTEGER", precision: 6, scale: 2, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BtwCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verkoopdocumentregels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Verkoopdocumentregels_Verkoopdocumenten_VerkoopdocumentId",
                        column: x => x.VerkoopdocumentId,
                        principalTable: "Verkoopdocumenten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Verkoopdocumenten_AdministratieId_Nummer",
                table: "Verkoopdocumenten",
                columns: new[] { "AdministratieId", "Nummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Verkoopdocumenten_AdministratieId_Status_Datum",
                table: "Verkoopdocumenten",
                columns: new[] { "AdministratieId", "Status", "Datum" });

            migrationBuilder.CreateIndex(
                name: "IX_Verkoopdocumentregels_VerkoopdocumentId",
                table: "Verkoopdocumentregels",
                column: "VerkoopdocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Verkoopdocumentregels");

            migrationBuilder.DropTable(
                name: "Verkoopdocumenten");

            migrationBuilder.DropColumn(
                name: "Betalingstermijn",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Contactpersoon",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Klant",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Land",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Omzetrekening",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Plaats",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Postcode",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Straat",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Verkoopbtwcode",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Land",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Plaats",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Postcode",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Straat",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Telefoon",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Administraties");
        }
    }
}
