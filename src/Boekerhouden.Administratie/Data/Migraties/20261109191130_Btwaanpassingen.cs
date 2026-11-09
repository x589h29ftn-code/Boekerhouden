using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Btwaanpassingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Btwaanpassingen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Jaar = table.Column<int>(type: "integer", nullable: false),
                    Sleutel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Bedrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GewijzigdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Btwaanpassingen", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Btwaanpassingen_AdministratieId_Jaar_Sleutel",
                table: "Btwaanpassingen",
                columns: new[] { "AdministratieId", "Jaar", "Sleutel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Btwaanpassingen");
        }
    }
}
