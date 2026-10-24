using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Bespreking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Besprekingen",
                columns: table => new
                {
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: true),
                    Aanwezigen = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notitie = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ErkendDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ErkendOp = table.Column<DateOnly>(type: "date", nullable: true),
                    Wijze = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Besprekingen", x => x.DossierId);
                    table.ForeignKey(
                        name: "FK_Besprekingen_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Besprekingen");
        }
    }
}
