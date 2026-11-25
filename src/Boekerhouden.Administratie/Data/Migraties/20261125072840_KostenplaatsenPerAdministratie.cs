using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class KostenplaatsenPerAdministratie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Kostenplaatsen",
                table: "Administraties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Stond de module bij het kantoor aan, dan blijven kostenplaatsen aan bij de administraties die ze al gebruiken.
            migrationBuilder.Sql(
                """
                UPDATE "Administraties" SET "Kostenplaatsen" = TRUE
                WHERE EXISTS (SELECT 1 FROM "Kantoorinstellingen" k WHERE k."Kostenplaatsen" = TRUE)
                  AND EXISTS (SELECT 1 FROM "Kostenplaatsen" p WHERE p."AdministratieId" = "Administraties"."Id");
                """);

            migrationBuilder.DropColumn(
                name: "Kostenplaatsen",
                table: "Kantoorinstellingen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Kostenplaatsen",
                table: "Kantoorinstellingen",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""UPDATE "Kantoorinstellingen" SET "Kostenplaatsen" = EXISTS (SELECT 1 FROM "Administraties" WHERE "Kostenplaatsen");""");

            migrationBuilder.DropColumn(
                name: "Kostenplaatsen",
                table: "Administraties");
        }
    }
}
