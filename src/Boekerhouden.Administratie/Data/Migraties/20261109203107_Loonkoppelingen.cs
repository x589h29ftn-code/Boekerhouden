using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Loonkoppelingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Loonkoppelingen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Loonrekening = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Loonnaam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loonkoppelingen", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Loonkoppelingen_AdministratieId_Loonrekening",
                table: "Loonkoppelingen",
                columns: new[] { "AdministratieId", "Loonrekening" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Loonkoppelingen");
        }
    }
}
