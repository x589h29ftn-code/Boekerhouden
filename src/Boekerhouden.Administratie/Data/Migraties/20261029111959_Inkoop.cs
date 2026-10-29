using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Inkoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Betalingstermijn",
                table: "Administraties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<string>>(
                name: "Ibans",
                table: "Administraties",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LimietAutomatisch",
                table: "Administraties",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Documenten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bestandsnaam = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Inhoudstype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Grootte = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Herkend = table.Column<string>(type: "jsonb", nullable: true),
                    Melding = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IngelezenOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngelezenDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BoekingId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documenten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documenten_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Leerkeuzes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sleutel = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BtwCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Tijdstip = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leerkeuzes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenstaandePosten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Soort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Relatie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Factuurnummer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Vervaldatum = table.Column<DateOnly>(type: "date", nullable: false),
                    Bedrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Openstaand = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Betalingskenmerk = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BoekingId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenstaandePosten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenstaandePosten_Boekingen_BoekingId",
                        column: x => x.BoekingId,
                        principalTable: "Boekingen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Regels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    Naam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BtwCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Automatisch = table.Column<bool>(type: "boolean", nullable: false),
                    Soort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Relatie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NaamBevat = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BedragVan = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    BedragTot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OmschrijvingBevat = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TegenIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    Richting = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regels_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Relaties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Naam = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Ibans = table.Column<List<string>>(type: "text[]", nullable: false),
                    BtwNummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    KvkNummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Tegenrekening = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AangemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relaties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relaties_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentInhouden",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Inhoud = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentInhouden", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocumentInhouden_Documenten_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documenten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documenten_AdministratieId_Sha256",
                table: "Documenten",
                columns: new[] { "AdministratieId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_Documenten_AdministratieId_Status",
                table: "Documenten",
                columns: new[] { "AdministratieId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Leerkeuzes_AdministratieId_Sleutel_Id",
                table: "Leerkeuzes",
                columns: new[] { "AdministratieId", "Sleutel", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenstaandePosten_AdministratieId_Relatie_Factuurnummer",
                table: "OpenstaandePosten",
                columns: new[] { "AdministratieId", "Relatie", "Factuurnummer" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenstaandePosten_BoekingId",
                table: "OpenstaandePosten",
                column: "BoekingId");

            migrationBuilder.CreateIndex(
                name: "IX_Regels_AdministratieId",
                table: "Regels",
                column: "AdministratieId");

            migrationBuilder.CreateIndex(
                name: "IX_Relaties_AdministratieId_Code",
                table: "Relaties",
                columns: new[] { "AdministratieId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentInhouden");

            migrationBuilder.DropTable(
                name: "Leerkeuzes");

            migrationBuilder.DropTable(
                name: "OpenstaandePosten");

            migrationBuilder.DropTable(
                name: "Regels");

            migrationBuilder.DropTable(
                name: "Relaties");

            migrationBuilder.DropTable(
                name: "Documenten");

            migrationBuilder.DropColumn(
                name: "Betalingstermijn",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Ibans",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "LimietAutomatisch",
                table: "Administraties");
        }
    }
}
