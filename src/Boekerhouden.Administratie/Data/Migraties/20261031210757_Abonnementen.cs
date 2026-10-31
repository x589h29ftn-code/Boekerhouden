using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Naam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Relatie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Herhaling = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Begin = table.Column<DateOnly>(type: "date", nullable: false),
                    Einde = table.Column<DateOnly>(type: "date", nullable: true),
                    Volgende = table.Column<int>(type: "integer", nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Regels = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    Referentie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tekst = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LayoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    Verdelen = table.Column<bool>(type: "boolean", nullable: false),
                    Verdeelrekening = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Automatisch = table.Column<bool>(type: "boolean", nullable: false),
                    Actief = table.Column<bool>(type: "boolean", nullable: false),
                    AangemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LaatstGemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
