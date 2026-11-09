using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Concepten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Concepten",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Soort = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Sleutel = table.Column<Guid>(type: "TEXT", nullable: false),
                    Inhoud = table.Column<string>(type: "TEXT", nullable: false),
                    BewaardOp = table.Column<long>(type: "INTEGER", nullable: false),
                    BewaardDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Concepten", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Concepten_AdministratieId_Soort_Sleutel",
                table: "Concepten",
                columns: new[] { "AdministratieId", "Soort", "Sleutel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Concepten");
        }
    }
}
