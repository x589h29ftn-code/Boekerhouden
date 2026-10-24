using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class VergelijkendVerloop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VerloopZonderVorig",
                table: "RekeningPresentaties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Afschrijvingspercentages",
                columns: table => new
                {
                    KlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kolom = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    VanafBoekjaar = table.Column<int>(type: "integer", nullable: false),
                    Tekst = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Afschrijvingspercentages", x => new { x.KlantId, x.Kolom, x.VanafBoekjaar });
                    table.ForeignKey(
                        name: "FK_Afschrijvingspercentages_Klanten_KlantId",
                        column: x => x.KlantId,
                        principalTable: "Klanten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Afschrijvingspercentages");

            migrationBuilder.DropColumn(
                name: "VerloopZonderVorig",
                table: "RekeningPresentaties");
        }
    }
}
