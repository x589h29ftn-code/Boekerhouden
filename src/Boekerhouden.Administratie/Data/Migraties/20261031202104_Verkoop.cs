using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
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
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Contactpersoon",
                table: "Relaties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Relaties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Klant",
                table: "Relaties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Land",
                table: "Relaties",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Omzetrekening",
                table: "Relaties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plaats",
                table: "Relaties",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                table: "Relaties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Straat",
                table: "Relaties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verkoopbtwcode",
                table: "Relaties",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Administraties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Land",
                table: "Administraties",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plaats",
                table: "Administraties",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                table: "Administraties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Straat",
                table: "Administraties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefoon",
                table: "Administraties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Administraties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Verkoopdocumenten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nummer = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Relatie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Klantgegevens = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Vervaldatum = table.Column<DateOnly>(type: "date", nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Referentie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tekst = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Betalingskenmerk = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreditVoorId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoekingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Verdeling = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LayoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    AbonnementId = table.Column<Guid>(type: "uuid", nullable: true),
                    AangemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefinitiefOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DefinitiefDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VerkoopdocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Aantal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Eenheid = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Prijs = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Korting = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BtwCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
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
