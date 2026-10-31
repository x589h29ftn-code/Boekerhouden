using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class VerkoopInlezen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternNummer",
                table: "Verkoopdocumenten",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Ingelezen",
                table: "Verkoopdocumenten",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Melding",
                table: "Verkoopdocumenten",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Verkoopbestanden",
                columns: table => new
                {
                    VerkoopdocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Bestandsnaam = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Inhoudstype = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Inhoud = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verkoopbestanden", x => x.VerkoopdocumentId);
                    table.ForeignKey(
                        name: "FK_Verkoopbestanden_Verkoopdocumenten_VerkoopdocumentId",
                        column: x => x.VerkoopdocumentId,
                        principalTable: "Verkoopdocumenten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Verkoopbestanden_AdministratieId_Sha256",
                table: "Verkoopbestanden",
                columns: new[] { "AdministratieId", "Sha256" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Verkoopbestanden");

            migrationBuilder.DropColumn(
                name: "ExternNummer",
                table: "Verkoopdocumenten");

            migrationBuilder.DropColumn(
                name: "Ingelezen",
                table: "Verkoopdocumenten");

            migrationBuilder.DropColumn(
                name: "Melding",
                table: "Verkoopdocumenten");
        }
    }
}
