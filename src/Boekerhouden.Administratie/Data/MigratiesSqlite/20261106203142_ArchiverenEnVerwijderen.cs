using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class ArchiverenEnVerwijderen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GearchiveerdDoor",
                table: "Administraties",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GearchiveerdOp",
                table: "Administraties",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VerwijderdeAdministraties",
                columns: table => new
                {
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    VerwijderdOp = table.Column<long>(type: "INTEGER", nullable: false),
                    VerwijderdDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerwijderdeAdministraties", x => x.AdministratieId);
                });

            // Een verwijderde administratie (rij in VerwijderdeAdministraties) mag zijn boekingen kwijt; verder blijven
            // boekingen onwijzigbaar. Het wijzigingslog blijft altijd onwijzigbaar.
            migrationBuilder.Sql("""
                DROP TRIGGER boekingen_onwijzigbaar_delete;
                CREATE TRIGGER boekingen_onwijzigbaar_delete BEFORE DELETE ON "Boekingen"
                WHEN NOT EXISTS (SELECT 1 FROM "VerwijderdeAdministraties" v WHERE v."AdministratieId" = OLD."AdministratieId")
                BEGIN SELECT RAISE(ABORT, 'Gegevens in Boekingen kunnen niet worden gewijzigd of verwijderd.'); END;
                DROP TRIGGER boekingsregels_onwijzigbaar_delete;
                CREATE TRIGGER boekingsregels_onwijzigbaar_delete BEFORE DELETE ON "Boekingsregels"
                WHEN NOT EXISTS (SELECT 1 FROM "VerwijderdeAdministraties" v WHERE v."AdministratieId" = OLD."AdministratieId")
                BEGIN SELECT RAISE(ABORT, 'Gegevens in Boekingsregels kunnen niet worden gewijzigd of verwijderd.'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER boekingen_onwijzigbaar_delete;
                CREATE TRIGGER boekingen_onwijzigbaar_delete BEFORE DELETE ON "Boekingen"
                BEGIN SELECT RAISE(ABORT, 'Gegevens in Boekingen kunnen niet worden gewijzigd of verwijderd.'); END;
                DROP TRIGGER boekingsregels_onwijzigbaar_delete;
                CREATE TRIGGER boekingsregels_onwijzigbaar_delete BEFORE DELETE ON "Boekingsregels"
                BEGIN SELECT RAISE(ABORT, 'Gegevens in Boekingsregels kunnen niet worden gewijzigd of verwijderd.'); END;
                """);

            migrationBuilder.DropTable(
                name: "VerwijderdeAdministraties");

            migrationBuilder.DropColumn(
                name: "GearchiveerdDoor",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "GearchiveerdOp",
                table: "Administraties");
        }
    }
}
