using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Review : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bijlagen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    StapId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bestandsnaam = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Grootte = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Door = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Op = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bijlagen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bijlagen_DossierStappen_StapId",
                        column: x => x.StapId,
                        principalTable: "DossierStappen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bijlagen_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reviewpunten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnderdeelId = table.Column<Guid>(type: "uuid", nullable: false),
                    StapId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tekst = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Door = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Op = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Opgelost = table.Column<bool>(type: "boolean", nullable: false),
                    Reactie = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OpgelostDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OpgelostOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviewpunten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reviewpunten_DossierOnderdelen_OnderdeelId",
                        column: x => x.OnderdeelId,
                        principalTable: "DossierOnderdelen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reviewpunten_DossierStappen_StapId",
                        column: x => x.StapId,
                        principalTable: "DossierStappen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Reviewpunten_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BijlageInhouden",
                columns: table => new
                {
                    BijlageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BijlageInhouden", x => x.BijlageId);
                    table.ForeignKey(
                        name: "FK_BijlageInhouden_Bijlagen_BijlageId",
                        column: x => x.BijlageId,
                        principalTable: "Bijlagen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bijlagen_DossierId",
                table: "Bijlagen",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_Bijlagen_StapId",
                table: "Bijlagen",
                column: "StapId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviewpunten_DossierId",
                table: "Reviewpunten",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviewpunten_OnderdeelId",
                table: "Reviewpunten",
                column: "OnderdeelId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviewpunten_StapId",
                table: "Reviewpunten",
                column: "StapId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BijlageInhouden");

            migrationBuilder.DropTable(
                name: "Reviewpunten");

            migrationBuilder.DropTable(
                name: "Bijlagen");
        }
    }
}
