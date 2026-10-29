using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Zoektemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Zoekplekken",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Relatie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Veld = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Pagina = table.Column<int>(type: "integer", nullable: false),
                    X0 = table.Column<double>(type: "double precision", nullable: false),
                    Y0 = table.Column<double>(type: "double precision", nullable: false),
                    X1 = table.Column<double>(type: "double precision", nullable: false),
                    Y1 = table.Column<double>(type: "double precision", nullable: false),
                    Voorrang = table.Column<bool>(type: "boolean", nullable: false),
                    Tijdstip = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zoekplekken", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Zoekplekken_AdministratieId_Relatie_Veld",
                table: "Zoekplekken",
                columns: new[] { "AdministratieId", "Relatie", "Veld" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Zoekplekken");
        }
    }
}
