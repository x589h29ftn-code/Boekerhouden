using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Concepten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Concepten",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Soort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Sleutel = table.Column<Guid>(type: "uuid", nullable: false),
                    Inhoud = table.Column<string>(type: "text", nullable: false),
                    BewaardOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BewaardDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Concepten", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Concepten_AdministratieId_Soort_Sleutel",
                table: "Concepten",
                columns: new[] { "AdministratieId", "Soort", "Sleutel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Concepten");
        }
    }
}
