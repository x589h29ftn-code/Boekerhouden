using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Soort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Bestandsnaam = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Grootte = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Paginas = table.Column<int>(type: "integer", nullable: false),
                    Indeling = table.Column<string>(type: "text", nullable: false),
                    Aangepast = table.Column<bool>(type: "boolean", nullable: false),
                    TeLezen = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IngelezenOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngelezenDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VerwerktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Facturen = table.Column<int>(type: "integer", nullable: true)
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
                    StapelId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Inhoud = table.Column<byte[]>(type: "bytea", nullable: false),
                    Tekstlaag = table.Column<string>(type: "text", nullable: true)
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
