using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class RapportViewer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Uitgeklapt",
                table: "PostPresentaties",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DossierToelichtingen",
                columns: table => new
                {
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sleutel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Tekst = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DossierToelichtingen", x => new { x.DossierId, x.Sleutel });
                    table.ForeignKey(
                        name: "FK_DossierToelichtingen_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UitsplitsingRegels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrootboekrekeningId = table.Column<Guid>(type: "uuid", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Bedrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BedragVorig = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UitsplitsingRegels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UitsplitsingRegels_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UitsplitsingRegels_Grootboekrekeningen_GrootboekrekeningId",
                        column: x => x.GrootboekrekeningId,
                        principalTable: "Grootboekrekeningen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UitsplitsingRegels_DossierId_GrootboekrekeningId_Volgorde",
                table: "UitsplitsingRegels",
                columns: new[] { "DossierId", "GrootboekrekeningId", "Volgorde" });

            migrationBuilder.CreateIndex(
                name: "IX_UitsplitsingRegels_GrootboekrekeningId",
                table: "UitsplitsingRegels",
                column: "GrootboekrekeningId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DossierToelichtingen");

            migrationBuilder.DropTable(
                name: "UitsplitsingRegels");

            migrationBuilder.DropColumn(
                name: "Uitgeklapt",
                table: "PostPresentaties");
        }
    }
}
