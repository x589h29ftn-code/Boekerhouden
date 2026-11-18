using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Stapels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stapels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Soort = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Bestandsnaam = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Grootte = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Paginas = table.Column<int>(type: "INTEGER", nullable: false),
                    Indeling = table.Column<string>(type: "TEXT", nullable: false),
                    Aangepast = table.Column<bool>(type: "INTEGER", nullable: false),
                    TeLezen = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IngelezenOp = table.Column<long>(type: "INTEGER", nullable: false),
                    IngelezenDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    VerwerktOp = table.Column<long>(type: "INTEGER", nullable: true),
                    Facturen = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stapels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stapels_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StapelInhouden",
                columns: table => new
                {
                    StapelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Inhoud = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Tekstlaag = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StapelInhouden", x => x.StapelId);
                    table.ForeignKey(
                        name: "FK_StapelInhouden_Stapels_StapelId",
                        column: x => x.StapelId,
                        principalTable: "Stapels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stapels_AdministratieId_Soort_Status",
                table: "Stapels",
                columns: new[] { "AdministratieId", "Soort", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StapelInhouden");

            migrationBuilder.DropTable(
                name: "Stapels");
        }
    }
}
