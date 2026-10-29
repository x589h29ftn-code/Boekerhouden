using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Zoektemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Zoekplekken",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Relatie = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Veld = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Pagina = table.Column<int>(type: "INTEGER", nullable: false),
                    X0 = table.Column<double>(type: "REAL", nullable: false),
                    Y0 = table.Column<double>(type: "REAL", nullable: false),
                    X1 = table.Column<double>(type: "REAL", nullable: false),
                    Y1 = table.Column<double>(type: "REAL", nullable: false),
                    Voorrang = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tijdstip = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zoekplekken", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Zoekplekken_AdministratieId_Relatie_Veld",
                table: "Zoekplekken",
                columns: new[] { "AdministratieId", "Relatie", "Veld" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Zoekplekken");
        }
    }
}
