using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
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
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Ingelezen",
                table: "Verkoopdocumenten",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Melding",
                table: "Verkoopdocumenten",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Verkoopbestanden",
                columns: table => new
                {
                    VerkoopdocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bestandsnaam = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Inhoudstype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Inhoud = table.Column<byte[]>(type: "bytea", nullable: false)
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
