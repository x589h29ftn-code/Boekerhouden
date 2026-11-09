using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Btwaanpassingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Btwaanpassingen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Jaar = table.Column<int>(type: "INTEGER", nullable: false),
                    Sleutel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Bedrag = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    GewijzigdOp = table.Column<long>(type: "INTEGER", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Btwaanpassingen", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Btwaanpassingen_AdministratieId_Jaar_Sleutel",
                table: "Btwaanpassingen",
                columns: new[] { "AdministratieId", "Jaar", "Sleutel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Btwaanpassingen");
        }
    }
}
