using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Werkprogramma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WerkprogrammaVersie",
                table: "Dossiers",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DossierOnderdelen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    BronOnderdeelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Soort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Titel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HoofdpostCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Verplicht = table.Column<bool>(type: "boolean", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    Notitie = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    GereviewdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GereviewdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DossierOnderdelen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DossierOnderdelen_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DossierPostKeuzes",
                columns: table => new
                {
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    HoofdpostCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Materieel = table.Column<bool>(type: "boolean", nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DossierPostKeuzes", x => new { x.DossierId, x.HoofdpostCode });
                    table.ForeignKey(
                        name: "FK_DossierPostKeuzes_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WerkprogrammaSjablonen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SoortOpdracht = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Naam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Versie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WerkprogrammaSjablonen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DossierStappen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OnderdeelId = table.Column<Guid>(type: "uuid", nullable: false),
                    BronStapId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titel = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Instructie = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Verplicht = table.Column<bool>(type: "boolean", nullable: false),
                    Controle = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Antwoord = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    Reden = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UitgevoerdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UitgevoerdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Verwijderd = table.Column<bool>(type: "boolean", nullable: false),
                    VerwijderdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VerwijderdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AangemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DossierStappen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DossierStappen_DossierOnderdelen_OnderdeelId",
                        column: x => x.OnderdeelId,
                        principalTable: "DossierOnderdelen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SjabloonOnderdelen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SjabloonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Soort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Titel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HoofdpostCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Verplicht = table.Column<bool>(type: "boolean", nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SjabloonOnderdelen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SjabloonOnderdelen_WerkprogrammaSjablonen_SjabloonId",
                        column: x => x.SjabloonId,
                        principalTable: "WerkprogrammaSjablonen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SjabloonStappen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OnderdeelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titel = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Instructie = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Verplicht = table.Column<bool>(type: "boolean", nullable: false),
                    Controle = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Volgorde = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SjabloonStappen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SjabloonStappen_SjabloonOnderdelen_OnderdeelId",
                        column: x => x.OnderdeelId,
                        principalTable: "SjabloonOnderdelen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DossierOnderdelen_DossierId",
                table: "DossierOnderdelen",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_DossierOnderdelen_DossierId_HoofdpostCode",
                table: "DossierOnderdelen",
                columns: new[] { "DossierId", "HoofdpostCode" },
                unique: true,
                filter: "\"HoofdpostCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DossierStappen_OnderdeelId",
                table: "DossierStappen",
                column: "OnderdeelId");

            migrationBuilder.CreateIndex(
                name: "IX_SjabloonOnderdelen_SjabloonId",
                table: "SjabloonOnderdelen",
                column: "SjabloonId");

            migrationBuilder.CreateIndex(
                name: "IX_SjabloonStappen_OnderdeelId",
                table: "SjabloonStappen",
                column: "OnderdeelId");

            migrationBuilder.CreateIndex(
                name: "IX_WerkprogrammaSjablonen_SoortOpdracht",
                table: "WerkprogrammaSjablonen",
                column: "SoortOpdracht",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DossierPostKeuzes");

            migrationBuilder.DropTable(
                name: "DossierStappen");

            migrationBuilder.DropTable(
                name: "SjabloonStappen");

            migrationBuilder.DropTable(
                name: "DossierOnderdelen");

            migrationBuilder.DropTable(
                name: "SjabloonOnderdelen");

            migrationBuilder.DropTable(
                name: "WerkprogrammaSjablonen");

            migrationBuilder.DropColumn(
                name: "WerkprogrammaVersie",
                table: "Dossiers");
        }
    }
}
