using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Rapport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Kantoorinstellingen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Naam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Adres = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Postcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Plaats = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Kleur = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Logo = table.Column<byte[]>(type: "bytea", nullable: true),
                    LogoType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kantoorinstellingen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostPresentaties",
                columns: table => new
                {
                    KlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sleutel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ToonSpecificatie = table.Column<bool>(type: "boolean", nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostPresentaties", x => new { x.KlantId, x.Sleutel });
                    table.ForeignKey(
                        name: "FK_PostPresentaties_Klanten_KlantId",
                        column: x => x.KlantId,
                        principalTable: "Klanten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RekeningPresentaties",
                columns: table => new
                {
                    GrootboekrekeningId = table.Column<Guid>(type: "uuid", nullable: false),
                    KlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Verbergen = table.Column<bool>(type: "boolean", nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RekeningPresentaties", x => x.GrootboekrekeningId);
                    table.ForeignKey(
                        name: "FK_RekeningPresentaties_Grootboekrekeningen_GrootboekrekeningId",
                        column: x => x.GrootboekrekeningId,
                        principalTable: "Grootboekrekeningen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RekeningPresentaties_Klanten_KlantId",
                        column: x => x.KlantId,
                        principalTable: "Klanten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RekeningPresentaties_KlantId",
                table: "RekeningPresentaties",
                column: "KlantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Kantoorinstellingen");

            migrationBuilder.DropTable(
                name: "PostPresentaties");

            migrationBuilder.DropTable(
                name: "RekeningPresentaties");
        }
    }
}
