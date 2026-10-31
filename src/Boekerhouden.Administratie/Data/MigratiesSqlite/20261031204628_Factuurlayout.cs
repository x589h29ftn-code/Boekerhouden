using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Factuurlayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Factuurlayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Standaard = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ontwerp = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    Logo = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Logotype = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    GewijzigdOp = table.Column<long>(type: "INTEGER", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Factuurlayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Factuurlayouts_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Factuurlayouts_AdministratieId",
                table: "Factuurlayouts",
                column: "AdministratieId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Factuurlayouts");
        }
    }
}
