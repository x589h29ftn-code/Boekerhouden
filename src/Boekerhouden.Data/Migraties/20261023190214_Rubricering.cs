using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Rubricering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EigenPosten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OuderCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Naam = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    AangemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EigenPosten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EigenPosten_Klanten_KlantId",
                        column: x => x.KlantId,
                        principalTable: "Klanten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KantoorPostNamen",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Naam = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KantoorPostNamen", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "KlantPostNamen",
                columns: table => new
                {
                    KlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Naam = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KlantPostNamen", x => new { x.KlantId, x.Code });
                    table.ForeignKey(
                        name: "FK_KlantPostNamen_Klanten_KlantId",
                        column: x => x.KlantId,
                        principalTable: "Klanten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RgsVersies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Versie = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Bestandsnaam = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AantalCodes = table.Column<int>(type: "integer", nullable: false),
                    Actief = table.Column<bool>(type: "boolean", nullable: false),
                    IngelezenOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngelezenDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RgsVersies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SjabloonRegels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Rekeningnummer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SjabloonRegels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Koppelingen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrootboekrekeningId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EigenPostId = table.Column<Guid>(type: "uuid", nullable: true),
                    RgsVersie = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Bron = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Koppelingen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Koppelingen_EigenPosten_EigenPostId",
                        column: x => x.EigenPostId,
                        principalTable: "EigenPosten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Koppelingen_Grootboekrekeningen_GrootboekrekeningId",
                        column: x => x.GrootboekrekeningId,
                        principalTable: "Grootboekrekeningen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Koppelingen_Klanten_KlantId",
                        column: x => x.KlantId,
                        principalTable: "Klanten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RgsCodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RgsVersieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Niveau = table.Column<int>(type: "integer", nullable: false),
                    Ouder = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Omschrijving = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OmschrijvingLang = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Kant = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    Omslagcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    Referentienummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Inactief = table.Column<bool>(type: "boolean", nullable: false),
                    Kiesfilters = table.Column<List<string>>(type: "text[]", nullable: false),
                    Wegfilters = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RgsCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RgsCodes_RgsVersies_RgsVersieId",
                        column: x => x.RgsVersieId,
                        principalTable: "RgsVersies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EigenPosten_KlantId_OuderCode",
                table: "EigenPosten",
                columns: new[] { "KlantId", "OuderCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Koppelingen_EigenPostId",
                table: "Koppelingen",
                column: "EigenPostId");

            migrationBuilder.CreateIndex(
                name: "IX_Koppelingen_GrootboekrekeningId",
                table: "Koppelingen",
                column: "GrootboekrekeningId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Koppelingen_KlantId",
                table: "Koppelingen",
                column: "KlantId");

            migrationBuilder.CreateIndex(
                name: "IX_RgsCodes_RgsVersieId_Code",
                table: "RgsCodes",
                columns: new[] { "RgsVersieId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RgsVersies_Actief",
                table: "RgsVersies",
                column: "Actief",
                unique: true,
                filter: "\"Actief\"");

            migrationBuilder.CreateIndex(
                name: "IX_RgsVersies_Sha256",
                table: "RgsVersies",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SjabloonRegels_Rekeningnummer",
                table: "SjabloonRegels",
                column: "Rekeningnummer",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KantoorPostNamen");

            migrationBuilder.DropTable(
                name: "KlantPostNamen");

            migrationBuilder.DropTable(
                name: "Koppelingen");

            migrationBuilder.DropTable(
                name: "RgsCodes");

            migrationBuilder.DropTable(
                name: "SjabloonRegels");

            migrationBuilder.DropTable(
                name: "EigenPosten");

            migrationBuilder.DropTable(
                name: "RgsVersies");
        }
    }
}
