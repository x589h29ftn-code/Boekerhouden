using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Doorlezen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bespreekpunten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Onderwerp = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Toelichting = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Signaal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Significant = table.Column<bool>(type: "boolean", nullable: false),
                    Uitkomst = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AangemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bespreekpunten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bespreekpunten_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DossierLeespunten",
                columns: table => new
                {
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sleutel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Titel = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Afhandeling = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Toelichting = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Door = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Op = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DossierLeespunten", x => new { x.DossierId, x.Sleutel });
                    table.ForeignKey(
                        name: "FK_DossierLeespunten_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bespreekpunten_DossierId",
                table: "Bespreekpunten",
                column: "DossierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bespreekpunten");

            migrationBuilder.DropTable(
                name: "DossierLeespunten");
        }
    }
}
