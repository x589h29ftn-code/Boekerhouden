using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Btwaangifte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Btwfrequentie",
                table: "Administraties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Kwartaal");

            migrationBuilder.CreateTable(
                name: "IngediendeAangiften",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Van = table.Column<DateOnly>(type: "date", nullable: false),
                    Tot = table.Column<DateOnly>(type: "date", nullable: false),
                    Rubrieken = table.Column<string>(type: "text", nullable: false),
                    Voorbelasting = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VastgelegdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VastgelegdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngediendeAangiften", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngediendeAangiften_AdministratieId_Van_Tot",
                table: "IngediendeAangiften",
                columns: new[] { "AdministratieId", "Van", "Tot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngediendeAangiften");

            migrationBuilder.DropColumn(
                name: "Btwfrequentie",
                table: "Administraties");
        }
    }
}
