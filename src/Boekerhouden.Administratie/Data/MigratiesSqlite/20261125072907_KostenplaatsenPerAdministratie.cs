using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
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
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Stond de module bij het kantoor aan, dan blijven kostenplaatsen aan bij de administraties die ze al gebruiken.
            migrationBuilder.Sql(
                """
                UPDATE "Administraties" SET "Kostenplaatsen" = 1
                WHERE EXISTS (SELECT 1 FROM "Kantoorinstellingen" k WHERE k."Kostenplaatsen" = 1)
                  AND EXISTS (SELECT 1 FROM "Kostenplaatsen" p WHERE p."AdministratieId" = "Administraties"."Id");
                """);

            // Zonder de tabel opnieuw op te bouwen (zoals EF op SQLite zou doen).
            migrationBuilder.Sql("""ALTER TABLE "Kantoorinstellingen" DROP COLUMN "Kostenplaatsen";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Kostenplaatsen",
                table: "Kantoorinstellingen",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""UPDATE "Kantoorinstellingen" SET "Kostenplaatsen" = EXISTS (SELECT 1 FROM "Administraties" WHERE "Kostenplaatsen" = 1);""");

            // Zonder de tabel opnieuw op te bouwen: dat zou de verwijzingen naar de administraties raken.
            migrationBuilder.Sql("""ALTER TABLE "Administraties" DROP COLUMN "Kostenplaatsen";""");
        }
    }
}
