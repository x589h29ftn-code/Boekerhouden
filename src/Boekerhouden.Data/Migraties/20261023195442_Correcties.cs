using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Correcties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AangemaaktBijCorrectie",
                table: "Grootboekrekeningen",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Correctieboekingen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nummer = table.Column<int>(type: "integer", nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Soort = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Terugkerend = table.Column<bool>(type: "boolean", nullable: false),
                    VerwerktBijKlant = table.Column<bool>(type: "boolean", nullable: false),
                    OvergenomenVanId = table.Column<Guid>(type: "uuid", nullable: true),
                    AangemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Correctieboekingen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Correctieboekingen_Correctieboekingen_OvergenomenVanId",
                        column: x => x.OvergenomenVanId,
                        principalTable: "Correctieboekingen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Correctieboekingen_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Correctieregels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectieboekingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    GrootboekrekeningId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bedrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Correctieregels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Correctieregels_Correctieboekingen_CorrectieboekingId",
                        column: x => x.CorrectieboekingId,
                        principalTable: "Correctieboekingen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Correctieregels_Grootboekrekeningen_GrootboekrekeningId",
                        column: x => x.GrootboekrekeningId,
                        principalTable: "Grootboekrekeningen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Correctieboekingen_DossierId_Nummer",
                table: "Correctieboekingen",
                columns: new[] { "DossierId", "Nummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Correctieboekingen_OvergenomenVanId",
                table: "Correctieboekingen",
                column: "OvergenomenVanId");

            migrationBuilder.CreateIndex(
                name: "IX_Correctieregels_CorrectieboekingId",
                table: "Correctieregels",
                column: "CorrectieboekingId");

            migrationBuilder.CreateIndex(
                name: "IX_Correctieregels_GrootboekrekeningId",
                table: "Correctieregels",
                column: "GrootboekrekeningId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Correctieregels");

            migrationBuilder.DropTable(
                name: "Correctieboekingen");

            migrationBuilder.DropColumn(
                name: "AangemaaktBijCorrectie",
                table: "Grootboekrekeningen");
        }
    }
}
