using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Loonkoppelingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Loonkoppelingen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Loonrekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Loonnaam = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loonkoppelingen", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Loonkoppelingen_AdministratieId_Loonrekening",
                table: "Loonkoppelingen",
                columns: new[] { "AdministratieId", "Loonrekening" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Loonkoppelingen");
        }
    }
}
