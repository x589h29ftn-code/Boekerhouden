using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Verloopoverzichten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Verloop",
                table: "RekeningPresentaties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Verloop",
                table: "PostPresentaties",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VerloopMutatieRegels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sleutel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    Kolom = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Bedrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerloopMutatieRegels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerloopMutatieRegels_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerloopMutatieRegels_DossierId_Sleutel_Volgorde",
                table: "VerloopMutatieRegels",
                columns: new[] { "DossierId", "Sleutel", "Volgorde" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerloopMutatieRegels");

            migrationBuilder.DropColumn(
                name: "Verloop",
                table: "RekeningPresentaties");

            migrationBuilder.DropColumn(
                name: "Verloop",
                table: "PostPresentaties");
        }
    }
}
