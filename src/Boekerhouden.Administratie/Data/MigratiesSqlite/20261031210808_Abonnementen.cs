using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Abonnementen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Abonnementen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Relatie = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Herhaling = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Begin = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Einde = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Volgende = table.Column<int>(type: "INTEGER", nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Regels = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    Referentie = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Tekst = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    LayoutId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Verdelen = table.Column<bool>(type: "INTEGER", nullable: false),
                    Verdeelrekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Automatisch = table.Column<bool>(type: "INTEGER", nullable: false),
                    Actief = table.Column<bool>(type: "INTEGER", nullable: false),
                    AangemaaktOp = table.Column<long>(type: "INTEGER", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LaatstGemaaktOp = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Abonnementen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Abonnementen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Abonnementen_AdministratieId_Actief",
                table: "Abonnementen",
                columns: new[] { "AdministratieId", "Actief" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Abonnementen");
        }
    }
}
