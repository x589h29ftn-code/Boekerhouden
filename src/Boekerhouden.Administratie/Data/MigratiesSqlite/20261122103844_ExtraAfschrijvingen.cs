using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class ExtraAfschrijvingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExtraAfschrijvingen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivumId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Datum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Bedrag = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Kostenrekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Boekstuk = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Tegenboeking = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    GeboektOp = table.Column<long>(type: "INTEGER", nullable: false),
                    GeboektDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtraAfschrijvingen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtraAfschrijvingen_Activa_ActivumId",
                        column: x => x.ActivumId,
                        principalTable: "Activa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExtraAfschrijvingen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtraAfschrijvingen_ActivumId",
                table: "ExtraAfschrijvingen",
                column: "ActivumId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtraAfschrijvingen_AdministratieId_Boekstuk",
                table: "ExtraAfschrijvingen",
                columns: new[] { "AdministratieId", "Boekstuk" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtraAfschrijvingen");
        }
    }
}
