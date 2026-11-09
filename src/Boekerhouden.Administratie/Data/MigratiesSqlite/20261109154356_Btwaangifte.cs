using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Btwaangifte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Btwfrequentie",
                table: "Administraties",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Kwartaal");

            migrationBuilder.CreateTable(
                name: "IngediendeAangiften",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Van = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Tot = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Rubrieken = table.Column<string>(type: "TEXT", nullable: false),
                    Voorbelasting = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    VastgelegdOp = table.Column<long>(type: "INTEGER", nullable: false),
                    VastgelegdDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngediendeAangiften", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngediendeAangiften_AdministratieId_Van_Tot",
                table: "IngediendeAangiften",
                columns: new[] { "AdministratieId", "Van", "Tot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngediendeAangiften");

            migrationBuilder.DropColumn(
                name: "Btwfrequentie",
                table: "Administraties");
        }
    }
}
