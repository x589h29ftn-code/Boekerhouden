using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Naam = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Standaard = table.Column<bool>(type: "boolean", nullable: false),
                    Ontwerp = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    Logo = table.Column<byte[]>(type: "bytea", nullable: true),
                    Logotype = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
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
