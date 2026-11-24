using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Kostenplaatsen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Kostenplaatsen",
                table: "Kantoorinstellingen",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kostenplaats",
                table: "Boekingsregels",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Kostenplaatsen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Naam = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Vervallen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kostenplaatsen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kostenplaatsen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Boekingsregels_AdministratieId_Kostenplaats",
                table: "Boekingsregels",
                columns: new[] { "AdministratieId", "Kostenplaats" });

            migrationBuilder.CreateIndex(
                name: "IX_Kostenplaatsen_AdministratieId_Code",
                table: "Kostenplaatsen",
                columns: new[] { "AdministratieId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Kostenplaatsen");

            migrationBuilder.DropIndex(
                name: "IX_Boekingsregels_AdministratieId_Kostenplaats",
                table: "Boekingsregels");

            migrationBuilder.DropColumn(
                name: "Kostenplaatsen",
                table: "Kantoorinstellingen");

            migrationBuilder.DropColumn(
                name: "Kostenplaats",
                table: "Boekingsregels");
        }
    }
}
