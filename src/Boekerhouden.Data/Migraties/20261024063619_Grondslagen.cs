using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Grondslagen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Grondslag",
                table: "Dossiers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DossierGrondslagen",
                columns: table => new
                {
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sleutel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Opgenomen = table.Column<bool>(type: "boolean", nullable: true),
                    Tekst = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DossierGrondslagen", x => new { x.DossierId, x.Sleutel });
                    table.ForeignKey(
                        name: "FK_DossierGrondslagen_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrondslagSjablonen",
                columns: table => new
                {
                    Sleutel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Groep = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Titel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TekstCommercieel = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TekstFiscaal = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Opname = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Codes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Jaargebonden = table.Column<bool>(type: "boolean", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrondslagSjablonen", x => x.Sleutel);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DossierGrondslagen");

            migrationBuilder.DropTable(
                name: "GrondslagSjablonen");

            migrationBuilder.DropColumn(
                name: "Grondslag",
                table: "Dossiers");
        }
    }
}
