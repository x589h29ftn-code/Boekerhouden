using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Werkplekken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Apparaten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sleutel = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Vingerafdruk = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WindowsNaam = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Computer = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AangevraagdOp = table.Column<long>(type: "INTEGER", nullable: false),
                    GebruikerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GekoppeldOp = table.Column<long>(type: "INTEGER", nullable: true),
                    GekoppeldDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LaatstGezien = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apparaten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Apparaten_AspNetUsers_GebruikerId",
                        column: x => x.GebruikerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Apparaten_GebruikerId",
                table: "Apparaten",
                column: "GebruikerId");

            migrationBuilder.CreateIndex(
                name: "IX_Apparaten_Vingerafdruk",
                table: "Apparaten",
                column: "Vingerafdruk",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Apparaten");
        }
    }
}
